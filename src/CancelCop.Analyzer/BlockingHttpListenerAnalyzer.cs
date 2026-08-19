using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>System.Net.HttpListener.GetContext</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC040
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>HttpListener.GetContext</c> parks a thread-pool thread until a request arrives.
/// That wait is unbounded and is not a <c>CancellationToken</c>. <c>GetContextAsync</c>
/// yields the thread.
/// </para>
/// <para>
/// <b>Why this is not CC036–CC039:</b> those rules are symbol-gated to Socket /
/// TcpClient / TcpListener / UdpClient. The HTTP listener is a fifth type —
/// verified empirically against the shipped analyzers.
/// </para>
/// <para>
/// The fixer rewrites a safe <c>GetContext()</c> to
/// <c>await GetContextAsync()</c>. The framework TAP is tokenless, so
/// the rewrite never invents a token argument.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(HttpListener listener, CancellationToken cancellationToken)
/// {
///     listener.GetContext();   // CC040
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingHttpListenerAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC040";

    /// <summary>
    /// Property key set when the diagnostic is correct but no safe rewrite exists.
    /// </summary>
    public const string NoFixProperty = "NoFix";

    private static readonly LocalizableString Title =
        "Avoid blocking HttpListener.GetContext in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'HttpListener.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "HttpListener.GetContext parks a thread-pool thread until a request arrives; in async code use GetContextAsync. The async form does not take a CancellationToken.";
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
            var listenerType = start.Compilation.GetTypeByMetadataName("System.Net.HttpListener");
            if (listenerType is null)
                return;

            var contextType = start.Compilation.GetTypeByMetadataName(
                "System.Net.HttpListenerContext"
            );

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, listenerType, contextType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol listenerType,
        INamedTypeSymbol? contextType
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
        if (invokedName is null || invokedName.Identifier.Text != "GetContext")
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
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, listenerType)
            || definition.Name != "GetContext"
        )
            return;

        if (listenerType.GetMembers("GetContextAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        if (CancellationTokenHelpers.AwaitInsertionIsUnsafe(context.SemanticModel, invocation))
            properties = properties.Add(NoFixProperty, "await-unsafe");

        if (
            !properties.ContainsKey(NoFixProperty)
            && IsInsideGetContextAsync(context, invocation, listenerType, contextType)
        )
            properties = properties.Add(NoFixProperty, "self-async");

        if (ResolvesToUsableCounterpart(context, invocation, contextType))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
            );
            return;
        }

        if (
            !ReachesCounterpart(
                ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
                contextType,
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

    private static bool IsInsideGetContextAsync(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol listenerType,
        INamedTypeSymbol? contextType
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
            enclosing is not { Name: "GetContextAsync" }
            || !IsHttpListenerOrDerived(enclosing.ContainingType, listenerType)
            || !IsUsableAsyncCounterpart(enclosing, contextType)
            || !MatchesGetContextShape(enclosing)
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
            var assigned = context
                .SemanticModel.GetSymbolInfo(
                    UnwrapIdentity(assignment.Left),
                    context.CancellationToken
                )
                .Symbol;
            if (!SymbolEqualityComparer.Default.Equals(assigned, symbol))
                continue;

            if (ExpressionMayAliasThis(context, assignment.Right, seen))
                return true;
        }

        return false;
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

    private static bool IsHttpListenerOrDerived(ITypeSymbol? type, INamedTypeSymbol listenerType)
    {
        while (type != null)
        {
            if (SymbolEqualityComparer.Default.Equals(type, listenerType))
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

    private static bool ResolvesToUsableCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol? contextType
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "GetContextAsync",
            tokenName: null
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
            && IsUsableAsyncCounterpart(bound, contextType)
            && MatchesGetContextShape(bound);
    }

    private static bool ReachesCounterpart(
        ITypeSymbol? receiverType,
        INamedTypeSymbol? contextType,
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
            foreach (var member in current.GetMembers("GetContextAsync").OfType<IMethodSymbol>())
            {
                if (within is not null && !compilation.IsSymbolAccessibleWithin(member, within))
                    continue;

                if (seen.Any(s => SameSignature(s, member)))
                    continue;

                seen.Add(member);
                if (IsUsableAsyncCounterpart(member, contextType) && MatchesGetContextShape(member))
                    return true;
            }
        }

        return false;
    }

    private static bool IsUsableAsyncCounterpart(
        IMethodSymbol? bound,
        INamedTypeSymbol? contextType
    )
    {
        if (bound is not { IsStatic: false, Name: "GetContextAsync" })
            return false;

        if (contextType is null || !IsTaskOf(bound.ReturnType, contextType))
            return false;

        if (bound.Parameters.IsEmpty)
            return true;

        var last = bound.Parameters[bound.Parameters.Length - 1];
        return CancellationTokenHelpers.IsCancellationToken(last.Type);
    }

    private static bool MatchesGetContextShape(IMethodSymbol tap) =>
        !tap.Parameters.Where(p => !CancellationTokenHelpers.IsCancellationToken(p.Type)).Any();

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

    private static bool IsTaskOf(ITypeSymbol type, ITypeSymbol expected)
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

            if (definition.Name is not ("Task" or "ValueTask") || current.TypeArguments.Length != 1)
                continue;

            return SymbolEqualityComparer.Default.Equals(current.TypeArguments[0], expected);
        }

        return false;
    }
}
