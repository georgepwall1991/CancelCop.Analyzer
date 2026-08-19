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
/// <c>System.Net.Mail.SmtpClient.Send</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC049
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>SmtpClient.Send</c> parks a thread-pool thread on an SMTP
/// handshake. That wait is not a <c>CancellationToken</c>.
/// <c>SendMailAsync</c> yields the thread. Token-taking
/// <c>SendMailAsync</c> is .NET 5+; .NET Framework has the tokenless
/// form. The TAP counterpart is <c>SendMailAsync</c>, not the
/// event-based <c>SendAsync</c>.
/// </para>
/// <para>
/// <b>Why this is not CC004:</b> CC004 is <c>HttpClient</c>.
/// <c>SmtpClient.Send</c> produced zero diagnostics from every shipped
/// rule — verified empirically. <c>Send</c> is not virtual; <c>new</c>
/// hiders match by inheritance plus the framework shape
/// (<c>MailMessage</c> or four strings).
/// </para>
/// <para>
/// The fixer rewrites a safe <c>Send</c> to <c>await SendMailAsync</c>,
/// flowing an in-scope token when the rewritten call still binds.
/// A this/base call or this-alias inside <c>SendMailAsync</c> is
/// reported without a rewrite.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
/// {
///     client.Send(message);   // CC049
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingSmtpClientAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC049";

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    /// <summary>
    /// Property key set when the diagnostic is correct but no safe rewrite exists, so the code fix
    /// must not offer one.
    /// </summary>
    public const string NoFixProperty = "NoFix";

    /// <summary>
    /// Property key for the TAP token parameter name when the original call
    /// already uses named arguments, so the fixer can emit
    /// <c>cancellationToken: token</c> instead of a positional token.
    /// </summary>
    public const string TokenArgumentNameProperty = "TokenArgumentName";

    private static readonly LocalizableString Title =
        "Avoid blocking SmtpClient.Send in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'SmtpClient.{0}' in async code; use 'SendMailAsync'";
    private static readonly LocalizableString Description =
        "SmtpClient.Send parks a thread-pool thread on an SMTP handshake; in async code use SendMailAsync. Token-taking SendMailAsync is .NET 5+; .NET Framework has the tokenless form. Do not use the event-based SendAsync.";
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
            var clientType = start.Compilation.GetTypeByMetadataName("System.Net.Mail.SmtpClient");
            if (clientType is null)
                return;

            var messageType = start.Compilation.GetTypeByMetadataName(
                "System.Net.Mail.MailMessage"
            );

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, clientType, messageType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol clientType,
        INamedTypeSymbol? messageType
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
        if (invokedName is null || invokedName.Identifier.Text != "Send")
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        if (!IsFrameworkSend(method, clientType, messageType))
            return;

        if (clientType.GetMembers("SendMailAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        if (CancellationTokenHelpers.AwaitInsertionIsUnsafe(context.SemanticModel, invocation))
            properties = properties.Add(NoFixProperty, "await-unsafe");

        if (
            !properties.ContainsKey(NoFixProperty)
            && IsInsideSendMailAsync(context, invocation, clientType, method)
        )
            properties = properties.Add(NoFixProperty, "self-async");

        var tokenName = CancellationTokenHelpers
            .FindEnclosingCancellationToken(invocation, context.SemanticModel)
            ?.ExpressionText;

        var tokenArgumentName =
            tokenName != null && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
                ? FindTokenParameterName(
                    ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
                    method,
                    context
                )
                : null;

        if (
            tokenName != null
            && !ResolvesToUsableCounterpart(
                context,
                invocation,
                method,
                tokenName,
                tokenArgumentName
            )
        )
        {
            tokenName = null;
            tokenArgumentName = null;
        }

        if (ResolvesToUsableCounterpart(context, invocation, method, tokenName, tokenArgumentName))
        {
            if (tokenName != null)
                properties = properties.Add(TokenNameProperty, tokenName);

            if (tokenArgumentName != null)
                properties = properties.Add(TokenArgumentNameProperty, tokenArgumentName);

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invokedName.GetLocation(), properties, method.Name)
            );
            return;
        }

        if (
            !ReachesCounterpart(
                ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
                method,
                context
            )
        )
            return;

        if (!properties.ContainsKey(NoFixProperty))
            properties = properties.Add(NoFixProperty, "token-required");

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, method.Name)
        );
    }

    private static bool IsInsideSendMailAsync(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol clientType,
        IMethodSymbol send
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
            enclosing is not { Name: "SendMailAsync" }
            || !IsOrInherits(enclosing.ContainingType, clientType)
            || !IsUsableAsyncCounterpart(enclosing)
            || !MatchesSendShape(enclosing, send)
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
            symbol is IFieldSymbol or IPropertySymbol
            && IsInstanceMemberOfEnclosingType(context, symbol, expression)
        )
            return FieldOrPropertyAssignedFromThis(context, symbol, seen);

        if (symbol is not (ILocalSymbol or IParameterSymbol))
            return false;

        return SymbolIsAssignedFromThis(context, symbol, expression, seen);
    }

    private static bool FieldOrPropertyAssignedFromThis(
        SyntaxNodeAnalysisContext context,
        ISymbol symbol,
        HashSet<ISymbol> seen
    )
    {
        if (!seen.Add(symbol))
            return false;

        var type = symbol.ContainingType;
        if (type is null)
            return false;

        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            // Stay on this tree. Compilation.GetSemanticModel is banned
            // in analyzers (RS1030); a ctor assignment in another
            // partial part is a documented stay-quiet miss.
            if (reference.SyntaxTree != context.SemanticModel.SyntaxTree)
                continue;

            if (reference.GetSyntax() is not TypeDeclarationSyntax typeSyntax)
                continue;

            foreach (
                var declarator in typeSyntax.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            )
            {
                if (
                    !SymbolEqualityComparer.Default.Equals(
                        context.SemanticModel.GetDeclaredSymbol(
                            declarator,
                            context.CancellationToken
                        ),
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

            foreach (
                var property in typeSyntax.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            )
            {
                if (
                    !SymbolEqualityComparer.Default.Equals(
                        context.SemanticModel.GetDeclaredSymbol(
                            property,
                            context.CancellationToken
                        ),
                        symbol
                    )
                )
                    continue;

                if (
                    property.ExpressionBody?.Expression is { } expr
                    && ExpressionMayAliasThis(context, expr, seen)
                )
                    return true;

                if (property.AccessorList is null)
                    continue;

                foreach (var accessor in property.AccessorList.Accessors)
                {
                    if (!accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                        continue;

                    if (
                        accessor.ExpressionBody?.Expression is { } getExpr
                        && ExpressionMayAliasThis(context, getExpr, seen)
                    )
                        return true;

                    if (accessor.Body is null)
                        continue;

                    foreach (
                        var ret in accessor.Body.DescendantNodes().OfType<ReturnStatementSyntax>()
                    )
                    {
                        if (
                            ret.Expression is { } retExpr
                            && ExpressionMayAliasThis(context, retExpr, seen)
                        )
                            return true;
                    }
                }
            }

            foreach (
                var assignment in typeSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            )
            {
                if (!IsAssignmentTo(context, assignment.Left, symbol))
                    continue;

                if (ExpressionMayAliasThis(context, assignment.Right, seen))
                    return true;
            }
        }

        return false;
    }

    private static bool IsInstanceMemberOfEnclosingType(
        SyntaxNodeAnalysisContext context,
        ISymbol? symbol,
        SyntaxNode location
    )
    {
        if (symbol is not (IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false }))
            return false;

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

        var body = EnclosingMethodBody(location);
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

    private static SyntaxNode? EnclosingMethodBody(SyntaxNode node)
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

    private static string? FindTokenParameterName(
        ITypeSymbol? receiverType,
        IMethodSymbol send,
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

        for (var current = receiverType; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers("SendMailAsync").OfType<IMethodSymbol>())
            {
                if (within is not null && !compilation.IsSymbolAccessibleWithin(member, within))
                    continue;

                if (!IsUsableAsyncCounterpart(member) || !MatchesSendShape(member, send))
                    continue;

                var last = member.Parameters[member.Parameters.Length - 1];
                if (CancellationTokenHelpers.IsCancellationToken(last.Type))
                    return last.Name;
            }
        }

        return "cancellationToken";
    }

    private static bool ResolvesToUsableCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol send,
        string? tokenName,
        string? tokenArgumentName = null
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "SendMailAsync",
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
            && MatchesSendShape(bound, send);
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

    private static bool IsUsableAsyncCounterpart(IMethodSymbol? bound)
    {
        if (bound is not { IsStatic: false, Name: "SendMailAsync" })
            return false;

        if (!IsTaskLike(bound.ReturnType))
            return false;

        if (bound.Parameters.Length == 0)
            return false;

        var last = bound.Parameters[bound.Parameters.Length - 1];
        if (CancellationTokenHelpers.IsCancellationToken(last.Type))
            return bound.Parameters.Length >= 2;

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

    private static bool ReachesCounterpart(
        ITypeSymbol? receiverType,
        IMethodSymbol send,
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
            foreach (var member in current.GetMembers("SendMailAsync").OfType<IMethodSymbol>())
            {
                if (within is not null && !compilation.IsSymbolAccessibleWithin(member, within))
                    continue;

                if (seen.Any(s => SameSignature(s, member)))
                    continue;

                seen.Add(member);

                if (IsUsableAsyncCounterpart(member) && MatchesSendShape(member, send))
                    return true;
            }
        }

        return false;
    }

    private static bool MatchesSendShape(IMethodSymbol tap, IMethodSymbol send)
    {
        var tapArgs = tap
            .Parameters.Where(p => !CancellationTokenHelpers.IsCancellationToken(p.Type))
            .ToArray();
        if (tapArgs.Length != send.Parameters.Length)
            return false;

        for (var i = 0; i < tapArgs.Length; i++)
        {
            if (tapArgs[i].RefKind != send.Parameters[i].RefKind)
                return false;

            if (!SymbolEqualityComparer.Default.Equals(tapArgs[i].Type, send.Parameters[i].Type))
                return false;
        }

        return true;
    }

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

    /// <summary>
    /// Match the framework <c>Send</c> overloads: instance, arity 0,
    /// <c>void</c>, either one <c>MailMessage</c> or four strings, on
    /// <c>SmtpClient</c> or a subclass. Custom helpers, generics, and
    /// statics stay quiet. <c>SendAsync</c> is a different name.
    /// </summary>
    private static bool IsFrameworkSend(
        IMethodSymbol method,
        INamedTypeSymbol clientType,
        INamedTypeSymbol? messageType
    )
    {
        if (method.IsStatic || method.Arity != 0)
            return false;

        if (!IsOrInherits(method.ContainingType, clientType))
            return false;

        if (method.ReturnType.SpecialType != SpecialType.System_Void)
            return false;

        if (method.Parameters.Any(p => p.RefKind != RefKind.None))
            return false;

        if (method.Parameters.Length == 1)
        {
            return messageType is not null
                && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, messageType);
        }

        if (method.Parameters.Length != 4)
            return false;

        return method.Parameters.All(p => p.Type.SpecialType == SpecialType.System_String);
    }

    private static bool IsOrInherits(INamedTypeSymbol? type, INamedTypeSymbol expected)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expected))
                return true;
        }

        return false;
    }
}
