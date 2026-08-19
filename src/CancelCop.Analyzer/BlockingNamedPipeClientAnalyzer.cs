using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking
/// <c>System.IO.Pipes.NamedPipeClientStream.Connect</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC042
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>NamedPipeClientStream.Connect</c> parks a thread-pool thread until the
/// server accepts (or a timeout elapses). That wait is not a
/// <c>CancellationToken</c>. <c>ConnectAsync</c> yields the thread; on modern
/// .NET it takes a token.
/// </para>
/// <para>
/// <b>Why this is not CC041:</b> CC041 is symbol-gated to
/// <c>NamedPipeServerStream.WaitForConnection</c>. The client connect is a
/// sibling type — verified empirically against the shipped analyzers.
/// </para>
/// <para>
/// The fixer rewrites a safe <c>Connect</c> to
/// <c>await ConnectAsync</c>, preserving an <c>int</c> or <c>TimeSpan</c>
/// timeout and flowing an in-scope token when the rewritten call still
/// binds. There is no tokenless <c>ConnectAsync(TimeSpan)</c>, so that
/// overload is reported without a rewrite when no token is in scope.
/// <c>NamedPipeClientStream</c> is sealed.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
/// {
///     client.Connect();   // CC042
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingNamedPipeClientAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC042";

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    /// <summary>
    /// Property key set when the diagnostic is correct but no safe rewrite exists.
    /// </summary>
    public const string NoFixProperty = "NoFix";

    /// <summary>
    /// Property key for the TAP token parameter name when the original call
    /// already uses named arguments.
    /// </summary>
    public const string TokenArgumentNameProperty = "TokenArgumentName";

    private static readonly LocalizableString Title =
        "Avoid blocking NamedPipeClientStream.Connect in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'NamedPipeClientStream.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "NamedPipeClientStream.Connect parks a thread-pool thread until the server accepts; in async code use ConnectAsync. The token-taking overload is modern .NET only. ConnectAsync(TimeSpan) requires a token.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var clientType = start.Compilation.GetTypeByMetadataName(
                "System.IO.Pipes.NamedPipeClientStream"
            );
            if (clientType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, clientType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol clientType
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var invokedName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            IdentifierNameSyntax identifier => identifier,
            _ => null,
        };
        if (invokedName is null || invokedName.Identifier.Text != "Connect")
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        var definition = method;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod;

        if (
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, clientType)
            || definition.Name != "Connect"
        )
            return;

        if (clientType.GetMembers("ConnectAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        if (CancellationTokenHelpers.AwaitInsertionIsUnsafe(context.SemanticModel, invocation))
            properties = properties.Add(NoFixProperty, "await-unsafe");

        if (
            !properties.ContainsKey(NoFixProperty)
            && IsInsideConnectAsync(context, invocation, clientType, definition)
        )
            properties = properties.Add(NoFixProperty, "self-async");

        var tokenName = CancellationTokenHelpers
            .FindEnclosingCancellationToken(invocation, context.SemanticModel)
            ?.ExpressionText;

        var tokenArgumentName =
            tokenName != null && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
                ? FindTokenParameterName(
                    ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
                    definition,
                    context
                )
                : null;

        if (
            tokenName != null
            && !ResolvesToUsableCounterpart(
                context,
                invocation,
                definition,
                tokenName,
                tokenArgumentName
            )
        )
        {
            tokenName = null;
            tokenArgumentName = null;
        }

        if (
            ResolvesToUsableCounterpart(
                context,
                invocation,
                definition,
                tokenName,
                tokenArgumentName
            )
        )
        {
            if (tokenName != null)
                properties = properties.Add(TokenNameProperty, tokenName);

            if (tokenArgumentName != null)
                properties = properties.Add(TokenArgumentNameProperty, tokenArgumentName);

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
            );
            return;
        }

        if (
            !ReachesCounterpart(
                ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
                definition,
                context
            )
        )
            return;

        if (!properties.ContainsKey(NoFixProperty))
            properties = properties.Add(NoFixProperty, "no-safe-rewrite");

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
        );
    }

    private static bool IsInsideConnectAsync(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol clientType,
        IMethodSymbol connect
    )
    {
        var enclosing =
            context.SemanticModel.GetEnclosingSymbol(
                invocation.SpanStart,
                context.CancellationToken
            ) as IMethodSymbol;

        while (
            enclosing is { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction }
        )
            enclosing = enclosing.ContainingSymbol as IMethodSymbol;

        if (
            enclosing is null
            || enclosing.Name != "ConnectAsync"
            || !IsNamedPipeClientOrDerived(enclosing.ContainingType, clientType)
            || !IsUsableAsyncCounterpart(enclosing)
            || !MatchesConnectShape(enclosing, connect)
        )
            return false;

        return invocation.Expression switch
        {
            IdentifierNameSyntax => true,
            MemberAccessExpressionSyntax member => ReceiverMayAliasThis(context, member.Expression),
            _ => false,
        };
    }

    private static bool ReceiverMayAliasThis(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax receiver
    ) =>
        ExpressionMayAliasThis(
            context,
            receiver,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default)
        );

    private static bool ExpressionMayAliasThis(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression,
        HashSet<ISymbol> seen
    )
    {
        expression = UnwrapIdentity(expression);

        switch (expression)
        {
            case ThisExpressionSyntax:
            case BaseExpressionSyntax:
                return true;
            case ConditionalExpressionSyntax conditional:
                return ExpressionMayAliasThis(context, conditional.WhenTrue, seen)
                    || ExpressionMayAliasThis(context, conditional.WhenFalse, seen);
            case MemberAccessExpressionSyntax member
                when UnwrapIdentity(member.Expression)
                    is ThisExpressionSyntax
                        or BaseExpressionSyntax:
                expression = member.Name;
                break;
        }

        if (expression is not SimpleNameSyntax)
            return false;

        var symbol = context
            .SemanticModel.GetSymbolInfo(expression, context.CancellationToken)
            .Symbol;

        if (
            symbol is IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false }
            && IsInstanceMemberOfEnclosingType(context, symbol, expression)
        )
            return true;

        if (symbol is not (ILocalSymbol or IParameterSymbol))
            return false;

        return SymbolIsAssignedFromThis(context, symbol, expression, seen);
    }

    private static bool IsInstanceMemberOfEnclosingType(
        SyntaxNodeAnalysisContext context,
        ISymbol symbol,
        SyntaxNode location
    )
    {
        var enclosing =
            context.ContainingSymbol as INamedTypeSymbol
            ?? (context.ContainingSymbol as IMethodSymbol)?.ContainingType
            ?? context
                .SemanticModel.GetEnclosingSymbol(location.SpanStart, context.CancellationToken)
                ?.ContainingType;

        if (enclosing is null)
            return false;

        for (var current = enclosing; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, symbol.ContainingType))
                return true;
        }

        return false;
    }

    private static bool SymbolIsAssignedFromThis(
        SyntaxNodeAnalysisContext context,
        ISymbol symbol,
        SyntaxNode location,
        HashSet<ISymbol> seen
    )
    {
        if (!seen.Add(symbol))
            return false;

        var body = EnclosingFunctionBody(location);
        if (body is null)
            return false;

        foreach (var declarator in body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (
                !SymbolEqualityComparer.Default.Equals(
                    context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken),
                    symbol
                )
            )
                continue;

            if (
                declarator.Initializer?.Value is { } init
                && ExpressionMayAliasThis(context, init, seen)
            )
                return true;
        }

        foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (!IsAssignmentTo(context, assignment.Left, symbol))
                continue;

            if (ExpressionMayAliasThis(context, assignment.Right, seen))
                return true;
        }

        return false;
    }

    private static bool IsAssignmentTo(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax left,
        ISymbol symbol
    )
    {
        var assigned = context
            .SemanticModel.GetSymbolInfo(UnwrapIdentity(left), context.CancellationToken)
            .Symbol;
        return SymbolEqualityComparer.Default.Equals(assigned, symbol);
    }

    private static ExpressionSyntax UnwrapIdentity(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax paren:
                    expression = paren.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SuppressNullableWarningExpression
                } bang:
                    expression = bang.Operand;
                    continue;
                case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression } asExpr:
                    expression = asExpr.Left;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static SyntaxNode? EnclosingFunctionBody(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax { Body: { } body }:
                    return body;
                case MethodDeclarationSyntax { ExpressionBody: { } expr }:
                    return expr;
                case ConstructorDeclarationSyntax { Body: { } body }:
                    return body;
                case ConstructorDeclarationSyntax { ExpressionBody: { } expr }:
                    return expr;
                case AccessorDeclarationSyntax { Body: { } body }:
                    return body;
            }
        }

        return null;
    }

    private static bool IsNamedPipeClientOrDerived(ITypeSymbol? type, INamedTypeSymbol clientType)
    {
        while (type != null)
        {
            if (SymbolEqualityComparer.Default.Equals(type, clientType))
                return true;
            type = type.BaseType;
        }

        return false;
    }

    private static ITypeSymbol? ReceiverTypeOf(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation
    )
    {
        var receiver = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            MemberBindingExpressionSyntax => invocation
                .Ancestors()
                .OfType<ConditionalAccessExpressionSyntax>()
                .FirstOrDefault()
                ?.Expression,
            _ => null,
        };

        return receiver is null
            ? null
            : context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type;
    }

    private static string? FindTokenParameterName(
        ITypeSymbol? receiverType,
        IMethodSymbol connect,
        SyntaxNodeAnalysisContext context
    )
    {
        foreach (var member in ReachableConnectAsync(receiverType, context))
        {
            if (!MatchesConnectShape(member, connect))
                continue;

            if (member.Parameters.IsEmpty)
                continue;

            var last = member.Parameters[member.Parameters.Length - 1];
            if (CancellationTokenHelpers.IsCancellationToken(last.Type))
                return last.Name;
        }

        return "cancellationToken";
    }

    private static bool ResolvesToUsableCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol connect,
        string? tokenName,
        string? tokenArgumentName
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "ConnectAsync",
            tokenName,
            tokenArgumentName
        );
        if (speculative is null)
            return false;

        var bound =
            context
                .SemanticModel.GetSpeculativeSymbolInfo(
                    invocation.SpanStart,
                    speculative,
                    SpeculativeBindingOption.BindAsExpression
                )
                .Symbol as IMethodSymbol;
        return bound is not null
            && IsUsableAsyncCounterpart(bound)
            && MatchesConnectShape(bound, connect);
    }

    private static bool ReachesCounterpart(
        ITypeSymbol? receiverType,
        IMethodSymbol connect,
        SyntaxNodeAnalysisContext context
    )
    {
        foreach (var member in ReachableConnectAsync(receiverType, context))
        {
            if (IsUsableAsyncCounterpart(member) && MatchesConnectShape(member, connect))
                return true;
        }

        return false;
    }

    private static IEnumerable<IMethodSymbol> ReachableConnectAsync(
        ITypeSymbol? receiverType,
        SyntaxNodeAnalysisContext context
    )
    {
        var enclosing =
            context.ContainingSymbol
            ?? context.SemanticModel.GetEnclosingSymbol(
                context.Node.SpanStart,
                context.CancellationToken
            );
        var compilation = context.SemanticModel.Compilation;
        ISymbol within =
            enclosing as INamedTypeSymbol
            ?? enclosing?.ContainingType
            ?? (ISymbol)compilation.Assembly;
        var seen = new List<IMethodSymbol>();

        for (var current = receiverType; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers("ConnectAsync").OfType<IMethodSymbol>())
            {
                if (within is not null && !compilation.IsSymbolAccessibleWithin(member, within))
                    continue;

                if (seen.Any(s => SameSignature(s, member)))
                    continue;

                seen.Add(member);
                yield return member;
            }
        }
    }

    private static bool IsUsableAsyncCounterpart(IMethodSymbol? bound)
    {
        if (bound is not { IsStatic: false, Name: "ConnectAsync" })
            return false;

        if (!IsTaskLike(bound.ReturnType))
            return false;

        if (bound.Parameters.IsEmpty)
            return true;

        var last = bound.Parameters[bound.Parameters.Length - 1];
        if (CancellationTokenHelpers.IsCancellationToken(last.Type))
            return true;

        // Tokenless timeout TAP: ConnectAsync(int). There is no
        // ConnectAsync(TimeSpan) without a token.
        return bound.Parameters.Length == 1 && IsTimeoutType(bound.Parameters[0].Type);
    }

    private static bool MatchesConnectShape(IMethodSymbol tap, IMethodSymbol connect)
    {
        var tapArgs = tap
            .Parameters.Where(p => !CancellationTokenHelpers.IsCancellationToken(p.Type))
            .ToArray();
        if (tapArgs.Length != connect.Parameters.Length)
            return false;

        for (var i = 0; i < tapArgs.Length; i++)
        {
            if (tapArgs[i].RefKind != connect.Parameters[i].RefKind)
                return false;

            if (!SymbolEqualityComparer.Default.Equals(tapArgs[i].Type, connect.Parameters[i].Type))
                return false;
        }

        return true;
    }

    private static bool IsTimeoutType(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Int32
        || (type.Name == "TimeSpan" && type.ContainingNamespace?.ToDisplayString() == "System");

    private static bool SameSignature(IMethodSymbol left, IMethodSymbol right)
    {
        if (left.Parameters.Length != right.Parameters.Length)
            return false;

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (left.Parameters[i].RefKind != right.Parameters[i].RefKind)
                return false;

            if (
                !SymbolEqualityComparer.Default.Equals(
                    left.Parameters[i].Type,
                    right.Parameters[i].Type
                )
            )
                return false;
        }

        return true;
    }

    private static bool IsTaskLike(ITypeSymbol type)
    {
        for (
            var current = type as INamedTypeSymbol;
            current is not null;
            current = current.BaseType
        )
        {
            var definition = current.OriginalDefinition;
            if (definition.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks")
                continue;

            if (definition.Name is "Task" or "ValueTask")
                return true;
        }

        return false;
    }
}
