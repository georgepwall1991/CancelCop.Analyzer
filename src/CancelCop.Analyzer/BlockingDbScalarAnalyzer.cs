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
/// <c>System.Data.Common.DbCommand.ExecuteScalar</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC048
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>DbCommand.ExecuteScalar</c> parks a thread-pool thread on a
/// single-value query. That wait is not a <c>CancellationToken</c>.
/// <c>ExecuteScalarAsync</c> yields the thread and accepts a token (since
/// .NET Framework 4.5). The method is abstract, so overrides match;
/// <c>new</c> hiders still match by inheritance plus the framework shape
/// (including a more-derived return such as <c>string</c>). Custom
/// helpers, generic helpers, statics, <c>void</c> hiders, and
/// <c>Task</c>/<c>ValueTask</c> hiders stay quiet.
/// </para>
/// <para>
/// <b>Why this is not CC003, CC045, CC046, or CC047:</b> CC003 is EF Core.
/// CC045 is <c>DbConnection.Open</c>. CC046 is <c>ExecuteReader</c>. CC047
/// is <c>ExecuteNonQuery</c>. ADO.NET <c>ExecuteScalar</c> produced zero
/// diagnostics from every shipped rule — verified empirically.
/// </para>
/// <para>
/// The fixer rewrites a safe <c>ExecuteScalar()</c> to
/// <c>await ExecuteScalarAsync</c>, flowing an in-scope token when the
/// rewritten call still binds to a <c>Task&lt;T&gt;</c> TAP method.
/// A this/base call or a receiver that aliases this inside
/// <c>ExecuteScalarAsync</c> is reported without a rewrite.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
/// {
///     command.ExecuteScalar();   // CC048
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingDbScalarAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC048";

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    /// <summary>
    /// Property key set when the diagnostic is correct but no safe rewrite exists, so the code fix
    /// must not offer one.
    /// </summary>
    public const string NoFixProperty = "NoFix";

    private static readonly LocalizableString Title =
        "Avoid blocking DbCommand.ExecuteScalar in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'DbCommand.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "DbCommand.ExecuteScalar parks a thread-pool thread on a single-value query; in async code use ExecuteScalarAsync. ExecuteScalarAsync has accepted a CancellationToken since .NET Framework 4.5.";
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
            var commandType = start.Compilation.GetTypeByMetadataName(
                "System.Data.Common.DbCommand"
            );
            if (commandType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, commandType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol commandType
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
        if (invokedName is null || invokedName.Identifier.Text != "ExecuteScalar")
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        if (!IsFrameworkExecuteScalar(method, commandType))
            return;

        if (
            !TryGetExecuteScalarAsync(commandType, out var withToken, out var withoutToken)
            || (withToken is null && withoutToken is null)
        )
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        if (CancellationTokenHelpers.AwaitInsertionIsUnsafe(context.SemanticModel, invocation))
            properties = properties.Add(NoFixProperty, "await-unsafe");

        if (
            !properties.ContainsKey(NoFixProperty)
            && IsInsideExecuteScalarAsync(context, invocation, commandType)
        )
            properties = properties.Add(NoFixProperty, "self-async");

        var tokenName = CancellationTokenHelpers
            .FindEnclosingCancellationToken(invocation, context.SemanticModel)
            ?.ExpressionText;

        if (
            tokenName != null
            && (withToken is null || !ResolvesToUsableCounterpart(context, invocation, tokenName))
        )
        {
            tokenName = null;
        }

        var counterpart =
            tokenName != null
                ? withToken
                : withoutToken
                    ?? (
                        withToken is not null && withToken.Parameters.All(p => p.IsOptional)
                            ? withToken
                            : null
                    );

        if (counterpart is not null && ResolvesToUsableCounterpart(context, invocation, tokenName))
        {
            if (tokenName != null)
                properties = properties.Add(TokenNameProperty, tokenName);

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invokedName.GetLocation(), properties, method.Name)
            );
            return;
        }

        if (
            withToken is null
            || !ReachesCounterpart(
                ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
                1,
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

    private static bool IsInsideExecuteScalarAsync(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol commandType
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
            enclosing is not { Name: "ExecuteScalarAsync" }
            || !IsOrInherits(enclosing.ContainingType, commandType)
            || !IsUsableAsyncCounterpart(enclosing)
        )
            return false;

        return invocation.Expression switch
        {
            IdentifierNameSyntax => true,
            MemberAccessExpressionSyntax member => ReceiverMayAliasThis(context, member.Expression),
            _ => false,
        };
    }

    /// <summary>
    /// True when rewriting this receiver to <c>ExecuteScalarAsync</c> could
    /// re-enter the enclosing override. <c>this</c>, <c>base</c>, a cast of
    /// either, a local/parameter assigned from them, and an instance
    /// field/property of the enclosing type all count. A different command
    /// (factory result, unrelated parameter) does not.
    /// </summary>
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

        // A field or property on this may have been assigned this in a
        // constructor or another member. Stay quiet rather than rewrite
        // `_self.ExecuteScalar()` into a recursive TAP call.
        if (IsInstanceMemberOfEnclosingType(context, symbol, expression))
            return true;

        if (symbol is not (ILocalSymbol or IParameterSymbol))
            return false;

        return SymbolIsAssignedFromThis(context, symbol, expression, seen);
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
            ?? (
                context
                    .SemanticModel.GetEnclosingSymbol(location.SpanStart, context.CancellationToken)
                    ?.ContainingType
            );

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

        // Scan the enclosing method, not the nearest local function /
        // lambda. An alias declared in ExecuteScalarAsync and captured
        // by Inner() still re-enters the override if rewritten.
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

        // Any assignment from this in the enclosing method, including
        // later or unreachable ones. Flow-sensitive "not this at this
        // statement" would miss aliases; a fixer prefers stay-quiet.
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
                case AccessorDeclarationSyntax { Body: { } body }:
                    return body;
            }
        }

        return null;
    }

    private static bool IsFrameworkExecuteScalar(IMethodSymbol method, INamedTypeSymbol commandType)
    {
        if (method.IsStatic || method.Arity != 0)
            return false;

        if (!IsOrInherits(method.ContainingType, commandType))
            return false;

        if (
            method.ReturnType.SpecialType == SpecialType.System_Void
            || IsTaskLike(method.ReturnType)
        )
            return false;

        return method.Parameters.Length == 0;
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

    private static bool ResolvesToUsableCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string? tokenName
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "ExecuteScalarAsync",
            tokenName
        );

        if (speculative != null)
        {
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
                && AwaitedResultFitsUseSite(context, invocation, bound);
        }

        return ReachesCounterpart(
            ReceiverTypeOf(context, invocation),
            tokenName is null ? 0 : 1,
            context
        );
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

    /// <summary>
    /// Accept a TAP <c>ExecuteScalarAsync</c>: <c>Task&lt;T&gt;</c> /
    /// <c>ValueTask&lt;T&gt;</c> where <c>T</c> is a reference type
    /// (framework <c>object</c> or a covariant <c>string</c> hider),
    /// with either no parameters or a single <c>CancellationToken</c>.
    /// </summary>
    private static bool IsUsableAsyncCounterpart(IMethodSymbol? bound)
    {
        if (
            bound
            is not {
                IsStatic: false,
                Name: "ExecuteScalarAsync",
                ReturnType: INamedTypeSymbol
                {
                    IsGenericType: true,
                    Name: "Task" or "ValueTask",
                    TypeArguments.Length: 1,
                } task,
            }
        )
            return false;

        if (task.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks")
            return false;

        if (!task.TypeArguments[0].IsReferenceType)
            return false;

        if (bound.Parameters.Length == 0)
            return true;

        return bound.Parameters.Length == 1
            && CancellationTokenHelpers.IsCancellationToken(bound.Parameters[0].Type);
    }

    private static bool AwaitedResultFitsUseSite(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol bound
    )
    {
        if (invocation.Parent is ExpressionStatementSyntax)
            return true;

        if (bound.ReturnType is not INamedTypeSymbol { TypeArguments.Length: 1 } task)
            return false;

        var typeInfo = context.SemanticModel.GetTypeInfo(invocation, context.CancellationToken);

        // Speculative bind can see the framework Task<object> TAP while
        // a more-derived Task<string> hider is what the rewrite actually
        // calls. Walk reachable TAP members so a narrower T cannot
        // retarget an outer overload.
        if (
            typeInfo.Type is { } original
            && ReachableTapResultDiffers(
                ReceiverTypeOf(context, invocation) ?? bound.ReceiverType,
                original,
                bound,
                context
            )
        )
            return false;

        var converted = typeInfo.ConvertedType;
        if (converted is null)
            return true;

        var conversion = context.SemanticModel.Compilation.ClassifyConversion(
            task.TypeArguments[0],
            converted
        );
        return conversion.IsIdentity || conversion.IsImplicit;
    }

    private static bool ReachableTapResultDiffers(
        ITypeSymbol? receiverType,
        ITypeSymbol originalResult,
        IMethodSymbol emitted,
        SyntaxNodeAnalysisContext context
    )
    {
        foreach (var member in ReachableTapMembers(receiverType, context))
        {
            if (!SameSignature(member, emitted))
                continue;

            if (
                member.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } tap
                && !SymbolEqualityComparer.Default.Equals(tap.TypeArguments[0], originalResult)
            )
                return true;
        }

        return false;
    }

    private static IEnumerable<IMethodSymbol> ReachableTapMembers(
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
            foreach (var member in current.GetMembers("ExecuteScalarAsync").OfType<IMethodSymbol>())
            {
                if (within is not null && !compilation.IsSymbolAccessibleWithin(member, within))
                    continue;

                if (seen.Any(s => SameSignature(s, member)))
                    continue;

                seen.Add(member);

                if (IsUsableAsyncCounterpart(member))
                    yield return member;
            }
        }
    }

    private static bool ReachesCounterpart(
        ITypeSymbol? receiverType,
        int arity,
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
            foreach (var member in current.GetMembers("ExecuteScalarAsync").OfType<IMethodSymbol>())
            {
                if (within is not null && !compilation.IsSymbolAccessibleWithin(member, within))
                    continue;

                if (seen.Any(s => SameSignature(s, member)))
                    continue;

                seen.Add(member);

                var required = member.Parameters.Count(p => !p.IsOptional);
                if (arity < required || arity > member.Parameters.Length)
                    continue;

                if (IsUsableAsyncCounterpart(member))
                    return true;
            }
        }

        return false;
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

    private static bool TryGetExecuteScalarAsync(
        INamedTypeSymbol commandType,
        out IMethodSymbol? withToken,
        out IMethodSymbol? withoutToken
    )
    {
        withToken = null;
        withoutToken = null;

        foreach (var member in commandType.GetMembers("ExecuteScalarAsync"))
        {
            if (!IsUsableAsyncCounterpart(member as IMethodSymbol))
                continue;

            var candidate = (IMethodSymbol)member;
            if (candidate.DeclaredAccessibility != Accessibility.Public)
                continue;

            if (
                candidate.Parameters.Length == 1
                && CancellationTokenHelpers.IsCancellationToken(candidate.Parameters[0].Type)
            )
                withToken = candidate;
            else if (candidate.Parameters.Length == 0)
                withoutToken = candidate;
        }

        return withToken is not null || withoutToken is not null;
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
