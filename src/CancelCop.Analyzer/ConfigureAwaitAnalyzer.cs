using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects missing ConfigureAwait calls on awaited tasks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC012
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// In library code, not calling ConfigureAwait(false) can cause deadlocks when the library
/// is called from a synchronization context (UI apps, ASP.NET Classic). Library code should
/// not assume it needs to return to the original context.
/// </para>
/// <para>
/// <b>What it detects:</b>
/// <list type="bullet">
/// <item>await expressions without ConfigureAwait call</item>
/// <item>await foreach without ConfigureAwait call</item>
/// </list>
/// </para>
/// <para>
/// <b>Note:</b>
/// This rule is disabled by default and should be enabled in library projects via .editorconfig.
/// Application code typically wants to continue on the captured context.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation (in library code):
/// public async Task ProcessAsync()
/// {
///     await Task.Delay(1000);  // CC012
/// }
///
/// // Fixed:
/// public async Task ProcessAsync()
/// {
///     await Task.Delay(1000).ConfigureAwait(false);
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ConfigureAwaitAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC012";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "ConfigureAwait should be used";
    private static readonly LocalizableString MessageFormat = "Add ConfigureAwait(false) to this await expression";
    private static readonly LocalizableString Description = "In library code, ConfigureAwait(false) should be used to avoid capturing the synchronization context and prevent potential deadlocks.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: false, // Disabled by default - enable in library projects
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAwaitExpression, SyntaxKind.AwaitExpression);
    }

    private static void AnalyzeAwaitExpression(SyntaxNodeAnalysisContext context)
    {
        var awaitExpression = (AwaitExpressionSyntax)context.Node;

        // Skip if already has ConfigureAwait
        if (HasConfigureAwait(awaitExpression.Expression))
            return;

        // Check if the awaited expression is a Task or ValueTask
        var typeInfo = context.SemanticModel.GetTypeInfo(awaitExpression.Expression);
        if (!IsAwaitableTask(typeInfo.Type))
            return;

        var diagnostic = Diagnostic.Create(
            Rule,
            awaitExpression.AwaitKeyword.GetLocation());

        context.ReportDiagnostic(diagnostic);
    }

    private static bool HasConfigureAwait(ExpressionSyntax expression)
    {
        // Check if the expression ends with .ConfigureAwait(...)
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "ConfigureAwait")
        {
            return true;
        }

        return false;
    }

    private static bool IsAwaitableTask(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        var typeName = type.Name;
        var ns = type.ContainingNamespace?.ToString();

        // Task, Task<T>, ValueTask, ValueTask<T>
        if (ns == "System.Threading.Tasks")
        {
            if (typeName == "Task" || typeName == "ValueTask")
                return true;
        }

        // ConfiguredTaskAwaitable types should return false (already configured)
        if (typeName.Contains("ConfiguredTaskAwaitable") || typeName.Contains("ConfiguredValueTaskAwaitable"))
            return false;

        return false;
    }
}
