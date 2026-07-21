using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects an <c>async</c> lambda converted to a void-returning
/// delegate — an async-void lambda.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC024
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// When an <c>async</c> lambda is assigned to a void-returning delegate (or passed where one is
/// expected), the compiler binds it as <c>async void</c>: the caller cannot await it,
/// and an unhandled exception crashes the process. The classic trap is
/// <c>Parallel.ForEach(items, async item =&gt; await ...)</c>, where the body runs fire-and-forget.
/// Use a delegate that returns a task (<c>Func&lt;Task&gt;</c>) and an API that awaits it. This is
/// the lambda counterpart of CC023.
/// </para>
/// <para>
/// <b>What it detects:</b> an <c>async</c> lambda whose converted delegate type is
/// any void-returning delegate. Event-handler delegates and task-returning delegates are not
/// flagged.
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

    private static readonly LocalizableString Title = "Avoid async lambdas converted to void-returning delegates";
    private static readonly LocalizableString MessageFormat = "Async lambda is converted to a void-returning delegate and runs as async void; use a Task-returning delegate";
    private static readonly LocalizableString Description = "An async lambda assigned to a void-returning delegate runs as async void: it cannot be awaited and its exceptions crash the process.";
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

        context.RegisterSyntaxNodeAction(
            AnalyzeLambda,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousMethodExpression);
    }

    private void AnalyzeLambda(SyntaxNodeAnalysisContext context)
    {
        // Covers lambdas and `async delegate { }` anonymous methods alike.
        var lambda = (AnonymousFunctionExpressionSyntax)context.Node;
        if (!lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
            return;

        var convertedType = context.SemanticModel.GetTypeInfo(lambda, context.CancellationToken).ConvertedType;
        if (!IsUnsafeVoidDelegate(convertedType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, lambda.AsyncKeyword.GetLocation()));
    }

    private static bool IsUnsafeVoidDelegate(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Delegate,
                DelegateInvokeMethod: { ReturnsVoid: true } invokeMethod,
            })
        {
            return false;
        }

        return !IsEventHandlerShape(invokeMethod);
    }

    private static bool IsEventHandlerShape(IMethodSymbol invokeMethod)
    {
        if (invokeMethod.Parameters.Length != 2 ||
            invokeMethod.Parameters[0].Type.SpecialType != SpecialType.System_Object)
        {
            return false;
        }

        for (var type = invokeMethod.Parameters[1].Type; type != null; type = type.BaseType)
        {
            if (type.Name == "EventArgs" && type.ContainingNamespace?.ToDisplayString() == "System")
                return true;
        }

        return false;
    }
}
