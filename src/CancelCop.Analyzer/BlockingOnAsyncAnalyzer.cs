using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects blocking on a task (<c>.Result</c>, <c>.Wait()</c>,
/// <c>.GetAwaiter().GetResult()</c>) inside an <c>async</c> function.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC015
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Blocking on a task synchronously ties up the thread, can deadlock when a synchronization context
/// is present, and swallows cancellation into an <c>AggregateException</c>. In an <c>async</c>
/// method the task should be <c>await</c>ed instead.
/// </para>
/// <para>
/// <b>What it detects:</b> <c>task.Result</c>, potentially blocking <c>task.Wait(...)</c>, and
/// <c>task.GetAwaiter().GetResult()</c> on a <c>Task</c>/<c>Task&lt;T&gt;</c>/<c>ValueTask</c> inside
/// an <c>async</c> method, local function, lambda, or anonymous method.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// public async Task&lt;int&gt; RunAsync()
///     => GetValueAsync().Result;            // CC015
///
/// // Fixed:
/// public async Task&lt;int&gt; RunAsync()
///     => await GetValueAsync();
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingOnAsyncAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC015";

    private static readonly LocalizableString Title = "Avoid blocking on async code";
    private static readonly LocalizableString MessageFormat = "Blocking on a task with '{0}' can deadlock; await the task instead";
    private static readonly LocalizableString Description = "Synchronously blocking on a task (.Result/.Wait()/.GetAwaiter().GetResult()) inside async code can deadlock and discards cancellation; await the task instead.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMemberBinding, SyntaxKind.MemberBindingExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        if (memberAccess.Name.Identifier.Text != "Result")
            return;

        // `task.Result` where the property is a Task<T>/ValueTask<T> result accessor.
        if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IPropertySymbol property)
            return;
        if (!IsTaskLike(property.ContainingType))
            return;

        Report(context, memberAccess.Name, ".Result");
    }

    private void AnalyzeMemberBinding(SyntaxNodeAnalysisContext context)
    {
        var memberBinding = (MemberBindingExpressionSyntax)context.Node;
        if (memberBinding.Name.Identifier.Text != "Result")
            return;

        if (context.SemanticModel.GetSymbolInfo(memberBinding, context.CancellationToken).Symbol is not IPropertySymbol property)
            return;
        if (!IsTaskLike(property.ContainingType))
            return;

        Report(context, memberBinding.Name, ".Result");
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        // Potentially blocking task.Wait() overloads. A constant zero-millisecond timeout is an
        // immediate completion probe; the code fix only rewrites the parameterless overload.
        if (method.Name == "Wait" && IsTaskLike(method.ContainingType))
        {
            if (HasZeroTimeout(invocation, context.SemanticModel, context.CancellationToken))
                return;

            Report(context, memberAccess.Name, ".Wait()");
            return;
        }

        // Task.WaitAll(...) / Task.WaitAny(...) — static blocking joins unless their timeout is
        // a guaranteed-zero immediate probe.
        if ((method.Name == "WaitAll" || method.Name == "WaitAny") && IsTaskLike(method.ContainingType))
        {
            if (HasZeroTimeout(invocation, context.SemanticModel, context.CancellationToken))
                return;

            Report(context, memberAccess.Name, "." + method.Name + "()");
            return;
        }

        // task.GetAwaiter().GetResult()
        if (method.Name == "GetResult" && IsTaskAwaiter(method.ContainingType))
        {
            Report(context, memberAccess.Name, ".GetAwaiter().GetResult()");
        }
    }

    private static bool HasZeroTimeout(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            return false;

        var timeSpanType = semanticModel.Compilation.GetTypeByMetadataName("System.TimeSpan");

        foreach (var argument in operation.Arguments)
        {
            if (argument.Parameter?.Name == "millisecondsTimeout" &&
                argument.Value.ConstantValue is { HasValue: true, Value: int value } &&
                value == 0)
            {
                return true;
            }

            if (!SymbolEqualityComparer.Default.Equals(argument.Parameter?.Type, timeSpanType))
                continue;

            var argumentValue = UnwrapImplicitOperations(argument.Value);
            if (argumentValue is IDefaultValueOperation)
                return true;

            if (argumentValue is IFieldReferenceOperation
                {
                    Field: { IsStatic: true, Name: "Zero" } field,
                } && SymbolEqualityComparer.Default.Equals(field.ContainingType, timeSpanType))
            {
                return true;
            }

            if (argumentValue is IObjectCreationOperation creation &&
                creation.Arguments.Length == 0 &&
                SymbolEqualityComparer.Default.Equals(creation.Type, timeSpanType))
            {
                return true;
            }
        }

        return false;
    }

    private static IOperation UnwrapImplicitOperations(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation { IsImplicit: true } conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    private static void Report(SyntaxNodeAnalysisContext context, SyntaxNode location, string display)
    {
        if (!CancellationTokenHelpers.IsInAsyncFunction(context.Node))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location.GetLocation(), display));
    }

    private static bool IsTaskLike(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        var ns = type.ContainingNamespace?.ToDisplayString();
        return (type.Name == "Task" || type.Name == "ValueTask") && ns == "System.Threading.Tasks";
    }

    private static bool IsTaskAwaiter(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        // Covers the bare awaiters (TaskAwaiter/ValueTaskAwaiter) and the configured awaiters
        // produced by `.ConfigureAwait(...)` — so `task.ConfigureAwait(false).GetAwaiter().GetResult()`
        // is caught too. All live in System.Runtime.CompilerServices.
        if (type.ContainingNamespace?.ToDisplayString() != "System.Runtime.CompilerServices")
            return false;

        return type.Name is "TaskAwaiter" or "ValueTaskAwaiter" or
            "ConfiguredTaskAwaiter" or "ConfiguredValueTaskAwaiter";
    }
}
