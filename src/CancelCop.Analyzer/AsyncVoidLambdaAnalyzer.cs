using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects an <c>async</c> lambda converted to a void-returning <c>Action</c>
/// delegate — an async-void lambda.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC024
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// When an <c>async</c> lambda is assigned to <c>System.Action</c>/<c>Action&lt;T&gt;</c> (or passed
/// where one is expected), the compiler binds it as <c>async void</c>: the caller cannot await it,
/// and an unhandled exception crashes the process. The classic trap is
/// <c>Parallel.ForEach(items, async item =&gt; await ...)</c>, where the body runs fire-and-forget.
/// Use a delegate that returns a task (<c>Func&lt;Task&gt;</c>) and an API that awaits it. This is
/// the lambda counterpart of CC023.
/// </para>
/// <para>
/// <b>What it detects:</b> an <c>async</c> lambda whose converted delegate type is
/// <c>System.Action</c> or <c>System.Action&lt;…&gt;</c>. Event-handler delegates and
/// <c>Func&lt;Task&gt;</c> are not <c>Action</c>, so they are not flagged.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Action run = async () => await DoAsync();   // CC024 - runs as async void
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AsyncVoidLambdaAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC024";

    private static readonly LocalizableString Title = "Avoid async lambdas converted to Action";
    private static readonly LocalizableString MessageFormat = "Async lambda is converted to a void-returning Action and runs as async void; use a Func<Task>-style delegate";
    private static readonly LocalizableString Description = "An async lambda assigned to Action runs as async void: it cannot be awaited and its exceptions crash the process.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression);
    }

    private void AnalyzeLambda(SyntaxNodeAnalysisContext context)
    {
        var lambda = (AnonymousFunctionExpressionSyntax)context.Node;
        if (!lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
            return;

        var convertedType = context.SemanticModel.GetTypeInfo(lambda, context.CancellationToken).ConvertedType;
        if (!IsActionDelegate(convertedType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, lambda.AsyncKeyword.GetLocation()));
    }

    private static bool IsActionDelegate(ITypeSymbol? type)
    {
        return type is { TypeKind: TypeKind.Delegate, Name: "Action" } &&
               type.ContainingNamespace?.ToDisplayString() == "System";
    }
}
