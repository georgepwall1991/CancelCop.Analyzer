using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>System.Threading.Thread.Join</c>
/// inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC053
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>Thread.Join</c> parks the calling thread-pool thread until the joined
/// thread terminates — an unbounded wait in async code. Await the task that
/// represents the work instead of joining a raw thread.
/// </para>
/// <para>
/// Verified against the net9/net10 reference packs:
/// <c>System.Threading.Thread</c> declares only
/// <c>Join()</c>, <c>Join(int)</c>, and <c>Join(TimeSpan)</c> — none of them
/// virtual — and declares no TAP counterpart at all. The speculative rebind
/// to a hypothetical <c>JoinAsync</c> is retained so that if the framework
/// ever grows one, the rewrite lights up without an analyzer change; today
/// every diagnostic is reported without a rewrite, with the in-scope token
/// riding along for a future hoist.
/// <c>Thread</c> is also sealed on current .NET, so no user-derived
/// override or hider can participate: the receiver lineage check only ever
/// sees the framework type itself.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(Thread thread, CancellationToken cancellationToken)
/// {
///     thread.Join();   // CC053
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingThreadJoinAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC053";

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
        "Avoid blocking Thread.Join in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'Thread.{0}' in async code; await the task representing the work instead";
    private static readonly LocalizableString Description =
        "Thread.Join parks the calling thread-pool thread until the joined thread terminates; in async code await the task that represents the work. Thread declares no TAP JoinAsync counterpart on current .NET, so no rewrite is offered.";
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
            var threadType = start.Compilation.GetTypeByMetadataName(
                "System.Threading.Thread"
            );
            if (threadType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, threadType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol threadType
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
        if (
            invokedName is null
            || invokedName.Identifier.Text != "Join"
        )
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        // Thread.Join overloads are not virtual in the framework; the walk is
        // kept for symmetry with the family rules and resolves any derived
        // override back to its framework lineage.
        var definition = method;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod;

        if (
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, threadType)
            || definition.Name != "Join"
        )
            return;

        // Thread declares no JoinAsync on any shipped .NET (verified against
        // the net9/net10 ref packs), so unlike the sibling rules there is no
        // counterpart-existence gate: the rule still reports the blocking
        // call. The speculative rebind below stays so a future framework
        // JoinAsync enables the rewrite automatically.

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        if (
            CancellationTokenHelpers.AwaitInsertionIsUnsafe(
                context.SemanticModel,
                invocation
            )
        )
            properties = properties.Add(NoFixProperty, "await-unsafe");

        if (
            !properties.ContainsKey(NoFixProperty)
            && IsInsideJoinAsync(context, invocation, threadType)
            && !ReceiverIsProvablyFresh(context, invocation, threadType)
        )
            properties = properties.Add(NoFixProperty, "self-async");

        var tokenName = CancellationTokenHelpers
            .FindEnclosingCancellationToken(invocation, context.SemanticModel)
            ?.ExpressionText;

        var tokenArgumentName =
            tokenName != null && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
                ? FindTokenParameterName(threadType)
                : null;

        if (
            ResolvesToUsableCounterpart(
                context,
                invocation,
                threadType,
                tokenName,
                tokenArgumentName
            )
            || ResolvesToUsableCounterpart(context, invocation, threadType, null, null)
        )
        {
            // Token-taking rebind failed but the tokenless form binds: drop the token.
            if (
                !ResolvesToUsableCounterpart(
                    context,
                    invocation,
                    threadType,
                    tokenName,
                    tokenArgumentName
                )
            )
            {
                tokenName = null;
                tokenArgumentName = null;
            }

            if (tokenName != null)
                properties = properties.Add(TokenNameProperty, tokenName);

            if (tokenArgumentName != null)
                properties = properties.Add(TokenArgumentNameProperty, tokenArgumentName);

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
            );
            return;
        }

        // No speculative rebind is possible (no JoinAsync exists on Thread
        // today, or the shape is unusable), but the call IS blocking: report
        // without a rewrite. The in-scope token still rides along so the
        // fixer's statement hoist can offer a candidate it re-validates by
        // speculative binding.
        if (!properties.ContainsKey(NoFixProperty))
            properties = properties.Add(
                NoFixProperty,
                CancellationTokenHelpers.IsWhenNotNullOfConditionalAccess(invocation)
                    ? "conditional-access"
                    : "no-safe-rewrite"
            );

        var hoistTokenName =
            tokenName
            ?? CancellationTokenHelpers
                .FindEnclosingCancellationToken(invocation, context.SemanticModel)
                ?.ExpressionText;
        if (hoistTokenName != null && !properties.ContainsKey(TokenNameProperty))
            properties = properties.Add(TokenNameProperty, hoistTokenName);
        if (
            hoistTokenName != null
            && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
            && !properties.ContainsKey(TokenArgumentNameProperty)
        )
            properties = properties.Add(
                TokenArgumentNameProperty,
                FindTokenParameterName(threadType)
            );

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
        );
    }

    private static bool IsInsideJoinAsync(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol threadType
    )
    {
        // A bare `Join(...)` — or one on a receiver that is provably `this`
        // (`this`, `base`, or a local assigned from this) — inside a
        // JoinAsync-shaped member retargets the enclosing call itself and
        // recurses when the fix virtually dispatches. Withhold those.
        var enclosing =
            context
                .SemanticModel.GetEnclosingSymbol(
                    invocation.SpanStart,
                    context.CancellationToken
                )
                as IMethodSymbol;

        while (
            enclosing is { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction }
        )
            enclosing = enclosing.ContainingSymbol as IMethodSymbol;

        return enclosing is not null
            && enclosing.Name == "JoinAsync"
            && DerivesFromOrEquals(enclosing.ContainingType, threadType)
            && IsTaskLike(enclosing.ReturnType);
    }

    private static bool ReceiverIsProvablyFresh(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol threadType
    )
    {
        // A bare `Join(...)` IS an implicit-this call — never fresh.
        if (invocation.Expression is IdentifierNameSyntax)
            return false;

        ExpressionSyntax? receiver;
        if (invocation.Expression is MemberBindingExpressionSyntax)
        {
            // A `?.` spine surfaces as a member binding; the receiver is the
            // conditional access's operation (`self?.Join(...)`).
            receiver = null;
            for (
                var current = invocation.Parent;
                current is not null;
                current = current.Parent
            )
            {
                if (
                    current is ConditionalAccessExpressionSyntax conditional
                    && ReferenceEquals(invocation, conditional.WhenNotNull)
                )
                {
                    receiver = conditional.Expression;
                    break;
                }
            }

            if (receiver is null)
                return true;
        }
        else if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = memberAccess.Expression;
        }
        else
        {
            return true;
        }

        while (receiver is ParenthesizedExpressionSyntax parenthesized)
            receiver = parenthesized.Expression;
        // Only `new Thread(...)` is PROVABLY fresh: a derived construction
        // (`new Worker(...)`) may be the enclosing instance, and an invocation result
        // may be a factory that returns `this`. Anything else — this, base, locals,
        // parameters, fields, properties — could alias the enclosing instance and
        // recurse after the rewrite, so it is withheld.
        // Compare the constructed type by SYMBOL, not by name: a user type that
        // merely happens to be named Thread is not provably fresh.
        if (receiver is not ObjectCreationExpressionSyntax creation)
            return false;
        var createdType = context.SemanticModel.GetTypeInfo(creation.Type).Type;
        return createdType is not null
            && SymbolEqualityComparer.Default.Equals(createdType, threadType);
    }

    private static bool DerivesFromOrEquals(ITypeSymbol? type, INamedTypeSymbol baseType)
    {
        while (type != null)
        {
            if (SymbolEqualityComparer.Default.Equals(type, baseType))
                return true;
            type = type.BaseType;
        }

        return false;
    }

    private static string? FindTokenParameterName(INamedTypeSymbol threadType)
    {
        for (var current = threadType; current != null; current = current.BaseType)
        {
            foreach (
                var member in current
                    .GetMembers("JoinAsync")
                    .OfType<IMethodSymbol>()
            )
            {
                if (member.Parameters.IsEmpty)
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
        INamedTypeSymbol threadType,
        string? tokenName,
        string? tokenArgumentName
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "JoinAsync",
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
            && !bound.IsStatic
            && bound.Name == "JoinAsync"
            && IsTaskLike(bound.ReturnType)
            && ResolvesOnFrameworkThread(bound, threadType)
            && bound.Parameters.Count(p =>
                !CancellationTokenHelpers.IsCancellationToken(p.Type)
            ) == invocation.ArgumentList.Arguments.Count;
    }

    private static bool ResolvesOnFrameworkThread(
        IMethodSymbol bound,
        INamedTypeSymbol threadType
    )
    {
        // Walk overrides so a legitimate override of the framework TAP member keeps
        // its framework lineage; a same-named `new` hider has no override chain and
        // must declare on Thread itself to pass.
        var definition = bound.OriginalDefinition;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod.OriginalDefinition;

        return SymbolEqualityComparer.Default.Equals(definition.ContainingType, threadType);
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
