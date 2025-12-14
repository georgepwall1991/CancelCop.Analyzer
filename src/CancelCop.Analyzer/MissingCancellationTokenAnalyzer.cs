using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects public and protected async methods missing a CancellationToken parameter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC001
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Public async methods are entry points that callers use. Without a CancellationToken parameter,
/// callers cannot cancel long-running operations, leading to wasted resources, poor user experience,
/// and blocked graceful shutdowns.
/// </para>
/// <para>
/// <b>What it detects:</b>
/// <list type="bullet">
/// <item>Public async methods returning Task, Task&lt;T&gt;, ValueTask, or ValueTask&lt;T&gt;</item>
/// <item>Protected async methods (for derived class usage)</item>
/// </list>
/// </para>
/// <para>
/// <b>What it ignores:</b>
/// <list type="bullet">
/// <item>Private and internal methods (implementation details)</item>
/// <item>Methods that already have a CancellationToken parameter</item>
/// <item>Non-async methods</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// public async Task ProcessAsync() { }
///
/// // Fixed:
/// public async Task ProcessAsync(CancellationToken cancellationToken = default) { }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MissingCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC001";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Public async method should have CancellationToken parameter";
    private static readonly LocalizableString MessageFormat = "Public async method '{0}' should have a CancellationToken parameter";
    private static readonly LocalizableString Description = "Public async methods should accept a CancellationToken parameter to allow cancellation of async operations.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
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
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;

        // Check if method is async
        if (!methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;

        // Check if method is public or protected
        var isPublicOrProtected = methodDeclaration.Modifiers.Any(m =>
            m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword));

        if (!isPublicOrProtected)
            return;

        // Check if method returns Task, Task<T>, ValueTask, or ValueTask<T>
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
            return;

        if (!CancellationTokenHelpers.IsAsyncReturnType(methodSymbol.ReturnType))
            return;

        // Check if method already has CancellationToken parameter
        if (CancellationTokenHelpers.HasCancellationTokenParameter(methodSymbol))
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(Rule, methodDeclaration.Identifier.GetLocation(), methodDeclaration.Identifier.Text);
        context.ReportDiagnostic(diagnostic);
    }
}
