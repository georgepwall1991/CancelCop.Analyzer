using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects an explicit <c>CancellationToken.None</c> / <c>default</c> argument passed
/// to a <c>CancellationToken</c> parameter when a real token is in scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC012
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Passing <c>CancellationToken.None</c> (or <c>default</c>) to a call explicitly opts that call out
/// of cancellation. When the surrounding method already has a token to offer, this is usually an
/// oversight: the operation can no longer be cancelled, blocking shutdown. Reported as
/// <b>Info</b> because it is occasionally intentional (e.g. a best-effort cleanup that must run to
/// completion), so it is a suggestion rather than a warning.
/// </para>
/// <para>
/// <b>What it detects:</b> a call argument that is <c>default</c>, <c>default(CancellationToken)</c>,
/// or <c>CancellationToken.None</c>, including parenthesized and statically imported forms, and
/// binds to a <c>CancellationToken</c>, where an in-scope token parameter is available.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Suggestion:
/// public async Task RunAsync(CancellationToken cancellationToken)
///     => await DoAsync(CancellationToken.None);   // CC012: pass cancellationToken instead
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ExplicitNoneTokenAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC012";

    /// <summary>
    /// Property key used to pass the in-scope token parameter name to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    private static readonly LocalizableString Title = "Avoid passing CancellationToken.None when a token is in scope";
    private static readonly LocalizableString MessageFormat = "Passing '{0}' discards cancellation; pass the in-scope token '{1}' instead";
    private static readonly LocalizableString Description = "Passing CancellationToken.None or default to a call that could observe cancellation opts it out of cancellation even though an in-scope token is available.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeArgument, SyntaxKind.Argument);
    }

    private void AnalyzeArgument(SyntaxNodeAnalysisContext context)
    {
        var argument = (ArgumentSyntax)context.Node;

        // Only call/constructor arguments are interesting (an argument also appears in
        // element-access, etc.). BaseObjectCreationExpressionSyntax covers both `new T(...)` and the
        // target-typed `new(...)` form.
        if (argument.Parent is not ArgumentListSyntax { Parent: InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax })
            return;

        if (!IsNoneishToken(
                argument.Expression,
                context.SemanticModel,
                context.CancellationToken,
                out var displayText))
            return;

        // The argument must actually bind to a CancellationToken (a bare `default` only counts in a
        // token context).
        if (!CancellationTokenHelpers.IsCancellationToken(
                context.SemanticModel.GetTypeInfo(argument.Expression, context.CancellationToken).ConvertedType))
            return;

        // A real token must be available to offer instead; otherwise None/default is the only choice.
        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            argument, context.SemanticModel);
        if (tokenParameter == null)
            return;

        var properties = ImmutableDictionary<string, string?>.Empty.Add(TokenNameProperty, tokenParameter.Name);
        var diagnostic = Diagnostic.Create(
            Rule, argument.Expression.GetLocation(), properties, displayText, tokenParameter.Name);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Recognises the three "no cancellation" spellings: <c>default</c>,
    /// <c>default(CancellationToken)</c>, and <c>CancellationToken.None</c>.
    /// </summary>
    private static bool IsNoneishToken(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out string displayText)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        if (expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            displayText = "default";
            return true;
        }

        if (expression is DefaultExpressionSyntax)
        {
            displayText = "default";
            return true;
        }

        if (IsCancellationTokenNone(expression, semanticModel, cancellationToken))
        {
            displayText = "CancellationToken.None";
            return true;
        }

        displayText = string.Empty;
        return false;
    }

    private static bool IsCancellationTokenNone(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not IPropertySymbol
            {
                Name: "None",
                IsStatic: true,
            } property)
        {
            return false;
        }

        var cancellationTokenType = semanticModel.Compilation.GetTypeByMetadataName(
            "System.Threading.CancellationToken");
        return SymbolEqualityComparer.Default.Equals(property.ContainingType, cancellationTokenType);
    }
}
