using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects an async call whose task is discarded as a bare expression statement in
/// non-async code, so it can be neither awaited nor cancelled.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC032
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// A dropped task cannot be cancelled, cannot be waited on at shutdown, and its failure is never
/// observed — the exception surfaces later on an unrelated thread, or nowhere at all. Work started
/// this way outlives the request or host that started it, which is the same class of problem as a
/// token that is never passed.
/// </para>
/// <para>
/// <b>The gap this fills:</b> the compiler's CS4014 only fires <i>inside</i> an async method. In a
/// constructor, a synchronous method, or a non-async lambda — exactly where the mistake is easiest
/// to make, because there is no <c>await</c> available to reach for — it says nothing at all.
/// CC032 covers only that gap and stays quiet where CS4014 already reports.
/// </para>
/// <para>
/// <b>Why there is no code fix:</b> the right resolution depends on intent — make the caller async
/// and await, hand the task to something that observes it, or opt in deliberately. Analyzer-only,
/// like CC017, CC020, CC024, CC027, and CC031.
/// </para>
/// <para>
/// <b>Conservative by design:</b> a task that is assigned, returned, passed as an argument, or
/// explicitly discarded with <c>_ =</c> is not dropped, and none of those is flagged. <c>_ =</c> in
/// particular is the documented way to say "I know, and I mean it"; a rule that flagged the opt-in
/// would be impossible to satisfy.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public TestClass()
/// {
///     InitializeAsync();   // CC032: nothing awaits or cancels this, and CS4014 does not fire here
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnawaitedAsyncCallAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC032";

    private static readonly LocalizableString Title = "Async call is not awaited in non-async code";
    private static readonly LocalizableString MessageFormat =
        "The task returned by '{0}' is discarded; it cannot be awaited or cancelled";
    private static readonly LocalizableString Description =
        "A task dropped as a bare expression statement cannot be cancelled or awaited at shutdown, and its failure is never observed. The compiler's CS4014 only reports this inside async methods.";
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

        // Registering on the statement — rather than on every invocation — is what makes "the task
        // is discarded" a property of the syntax rather than something to infer: an expression
        // statement is precisely the position where a value goes nowhere.
        context.RegisterSyntaxNodeAction(
            AnalyzeExpressionStatement,
            SyntaxKind.ExpressionStatement
        );

        // An expression-bodied lambda has no statement to register on, but a void-returning one
        // discards the task just as completely: `Action a = () => SaveAsync();`.
        context.RegisterSyntaxNodeAction(
            AnalyzeExpressionBodiedLambda,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousMethodExpression
        );

        // The same applies to an expression-bodied member: `public Service() => InitializeAsync();`
        // has no statement either, and a constructor cannot be async, so CS4014 is silent.
        context.RegisterSyntaxNodeAction(
            AnalyzeExpressionBodiedMember,
            SyntaxKind.ArrowExpressionClause
        );
    }

    private static void AnalyzeExpressionBodiedMember(SyntaxNodeAnalysisContext context)
    {
        var arrowClause = (ArrowExpressionClauseSyntax)context.Node;

        if (UnwrapInvocation(arrowClause.Expression) is not { } invocation)
            return;

        // Only a void-returning member drops the task; `Task Run() => SaveAsync();` returns it.
        if (
            arrowClause.Parent is not { } member
            || context.SemanticModel.GetDeclaredSymbol(member, context.CancellationToken)
                is not IMethodSymbol memberSymbol
            || !memberSymbol.ReturnsVoid
        )
            return;

        Report(context, invocation, arrowClause.Expression);
    }

    /// <summary>
    /// Returns the invocation an expression ultimately performs, seeing through a null-conditional
    /// access — <c>worker?.StartAsync()</c> is an invocation wrapped in a conditional access, and
    /// its task is discarded just the same.
    /// </summary>
    private static InvocationExpressionSyntax? UnwrapInvocation(ExpressionSyntax expression) =>
        expression switch
        {
            InvocationExpressionSyntax invocation => invocation,
            ConditionalAccessExpressionSyntax conditional => UnwrapInvocation(
                conditional.WhenNotNull
            ),
            _ => null,
        };

    private static void AnalyzeExpressionStatement(SyntaxNodeAnalysisContext context)
    {
        var statement = (ExpressionStatementSyntax)context.Node;

        // Only a bare call. An assignment (including the explicit `_ =` discard) hands the task
        // somewhere, so it is not dropped by this statement.
        if (UnwrapInvocation(statement.Expression) is { } invocation)
            Report(context, invocation, statement.Expression);
    }

    private static void AnalyzeExpressionBodiedLambda(SyntaxNodeAnalysisContext context)
    {
        var lambda = (AnonymousFunctionExpressionSyntax)context.Node;

        if (lambda.Body is not ExpressionSyntax body || UnwrapInvocation(body) is not { } invocation)
            return;

        // A lambda converted to a Task-returning delegate hands the task to its caller, so nothing
        // is discarded there. Only a void-returning conversion drops it. (An `async` lambda
        // converted to void is CC024's finding, not this one.)
        if (
            context.SemanticModel.GetSymbolInfo(lambda, context.CancellationToken).Symbol
                is not IMethodSymbol lambdaSymbol
            || !lambdaSymbol.ReturnsVoid
            || lambdaSymbol.IsAsync
        )
            return;

        Report(context, invocation, body);
    }

    /// <summary>
    /// Applies the shared gates — the call must return an awaitable, and must not already be covered
    /// by CS4014 — and reports.
    /// </summary>
    /// <param name="invocation">The call whose task is discarded; used to resolve the symbol.</param>
    /// <param name="reportOn">
    /// The whole expression the reader sees. For a null-conditional call these differ — the
    /// invocation is only the part after the <c>?.</c>, and underlining that alone would point at a
    /// fragment rather than the statement.
    /// </param>
    private static void Report(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        ExpressionSyntax reportOn
    )
    {
        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        if (!IsDiscardedAsyncResult(method.ReturnType))
            return;

        // An expression-tree lambda's body is data, not code: it never runs, so nothing is
        // discarded.
        if (CancellationTokenHelpers.IsWithinExpressionTree(invocation, context.SemanticModel))
            return;

        // Inside an async function the compiler already reports CS4014, and a second diagnostic on
        // the same line would be pure noise — but only for the shapes CS4014 actually covers. It
        // says nothing about a discarded *awaiter* in any context, so those are still reported here.
        if (
            IsCoveredByCompilerWarning(method.ReturnType)
            && CancellationTokenHelpers.IsInAsyncFunction(invocation)
        )
            return;

        var invokedName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            IdentifierNameSyntax identifier => identifier,
            _ => null,
        };

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                reportOn.GetLocation(),
                invokedName?.Identifier.Text ?? method.Name
            )
        );
    }

    /// <summary>
    /// Returns <c>true</c> when discarding a value of <paramref name="type"/> drops in-flight async
    /// work.
    /// </summary>
    /// <remarks>
    /// Wider than "is it a Task": the async work has already started by the time any of these
    /// values exists, so dropping any of them loses the same thing.
    /// <list type="bullet">
    /// <item><c>Task</c>/<c>ValueTask</c>, including a subclass or a type parameter constrained to
    /// one — a custom <c>Task</c>-derived type is still awaitable and still dropped.</item>
    /// <item>The awaitables <c>ConfigureAwait</c> produces.</item>
    /// <item>The awaiters <c>GetAwaiter</c> produces: <c>SaveAsync().GetAwaiter();</c> starts the
    /// work and throws the awaiter away, and the compiler never reports it in either context.</item>
    /// </list>
    /// </remarks>
    /// <summary>
    /// Returns <c>true</c> for the value types CS4014 reports on when discarded inside an async
    /// function — <c>Task</c>/<c>ValueTask</c> and the awaitables <c>ConfigureAwait</c> produces.
    /// An awaiter is deliberately absent: the compiler never warns about discarding one.
    /// </summary>
    private static bool IsCoveredByCompilerWarning(ITypeSymbol? type) =>
        IsDiscardedAsyncResult(type)
        && type?.Name is not ("TaskAwaiter" or "ValueTaskAwaiter");

    private static bool IsDiscardedAsyncResult(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        if (CancellationTokenHelpers.IsAsyncReturnType(type))
            return true;

        if (
            type.ContainingNamespace?.ToDisplayString() == "System.Runtime.CompilerServices"
            && type.Name
                is "ConfiguredTaskAwaitable"
                    or "ConfiguredValueTaskAwaitable"
                    or "TaskAwaiter"
                    or "ValueTaskAwaiter"
        )
            return true;

        // A type parameter carries its awaitability through its constraints rather than a base type.
        if (type is ITypeParameterSymbol typeParameter)
            return typeParameter.ConstraintTypes.Any(IsDiscardedAsyncResult);

        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (CancellationTokenHelpers.IsAsyncReturnType(current))
                return true;
        }

        return false;
    }
}
