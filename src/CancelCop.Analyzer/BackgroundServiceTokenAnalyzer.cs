using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a <see cref="System.Threading.CancellationToken"/>-ignoring
/// <c>BackgroundService.ExecuteAsync</c> override.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC017
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// A <c>BackgroundService</c> receives a <c>stoppingToken</c> that the host signals on shutdown. An
/// <c>ExecuteAsync</c> body that never observes it will not stop when the application is shutting
/// down, hanging graceful shutdown until a forced timeout. Because <c>ExecuteAsync</c> is an
/// override, the broader CC016 rule deliberately skips it — this rule covers that specific,
/// high-value case.
/// </para>
/// <para>
/// <b>What it detects:</b> an <c>override</c> of <c>ExecuteAsync(CancellationToken)</c> on a type
/// deriving from <c>Microsoft.Extensions.Hosting.BackgroundService</c> whose body never references
/// the token parameter.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// protected override async Task ExecuteAsync(CancellationToken stoppingToken)  // CC017
/// {
///     while (true) { await DoWorkAsync(); }   // never observes stoppingToken
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BackgroundServiceTokenAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC017";

    private static readonly LocalizableString Title = "BackgroundService.ExecuteAsync ignores its stopping token";
    private static readonly LocalizableString MessageFormat = "ExecuteAsync never observes stopping token '{0}'; the service will not stop on shutdown";
    private static readonly LocalizableString Description = "A BackgroundService.ExecuteAsync override should observe its stoppingToken so the service stops when the host shuts down.";
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

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.Identifier.Text != "ExecuteAsync")
            return;

        SyntaxNode? body = method.Body ?? (SyntaxNode?)method.ExpressionBody;
        if (body == null)
            return;

        if (context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is not IMethodSymbol symbol)
            return;
        if (!symbol.IsOverride)
            return;
        if (!DerivesFromBackgroundService(symbol.ContainingType))
            return;

        var tokenParameter = symbol.Parameters.FirstOrDefault(p => CancellationTokenHelpers.IsCancellationToken(p.Type));
        if (tokenParameter == null)
            return;

        if (CancellationTokenHelpers.IsParameterReferenced(body, tokenParameter, context.SemanticModel, context.CancellationToken))
            return;

        // Report on the parameter's declaration if available, else the method identifier.
        var location = method.ParameterList.Parameters
            .FirstOrDefault(p => p.Identifier.Text == tokenParameter.Name)?.Identifier.GetLocation()
            ?? method.Identifier.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, tokenParameter.Name));
    }

    private static bool DerivesFromBackgroundService(INamedTypeSymbol? type)
    {
        for (var current = type?.BaseType; current != null; current = current.BaseType)
        {
            if (current.Name == "BackgroundService" &&
                current.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Hosting")
                return true;
        }

        return false;
    }
}
