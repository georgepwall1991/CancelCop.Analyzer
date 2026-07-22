using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects <c>await foreach</c> over an <c>IAsyncEnumerable&lt;T&gt;</c>
/// that does not flow an in-scope <c>CancellationToken</c> via <c>.WithCancellation(token)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC010
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// An async stream consumed with <c>await foreach</c> can block indefinitely waiting for the next
/// element. Unless a token is threaded into the enumeration, a cancelled operation cannot interrupt
/// the consumer, so shutdown stalls and resources stay pinned. The framework-blessed way to pass the
/// token to an async iterator is <c>source.WithCancellation(token)</c>, which routes it to the
/// producer's <c>[EnumeratorCancellation]</c> parameter.
/// </para>
/// <para>
/// <b>What it detects:</b> an <c>await foreach</c> whose source is statically typed as
/// <c>IAsyncEnumerable&lt;T&gt;</c> (or implements it), where a <c>CancellationToken</c> is in scope
/// and the source neither already passes a token argument nor is wrapped in a configured cancelable
/// enumerable (the result of the framework <c>.WithCancellation</c>/<c>.ConfigureAwait</c> APIs).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// await foreach (var item in source)            // CC010
///     Process(item);
///
/// // Fixed:
/// await foreach (var item in source.WithCancellation(cancellationToken))
///     Process(item);
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AsyncEnumerableCancellationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC010";

    /// <summary>
    /// Property key used to pass the in-scope token parameter name to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    private static readonly LocalizableString Title = "await foreach should flow a CancellationToken";
    private static readonly LocalizableString MessageFormat = "'await foreach' over an IAsyncEnumerable should pass {0} via .WithCancellation({0})";
    private static readonly LocalizableString Description = "Consuming an async stream with 'await foreach' should flow an in-scope CancellationToken via .WithCancellation(token) so the enumeration can be interrupted.";
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

        context.RegisterSyntaxNodeAction(AnalyzeForEach, SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement);
    }

    private void AnalyzeForEach(SyntaxNodeAnalysisContext context)
    {
        var forEach = (CommonForEachStatementSyntax)context.Node;

        // Only `await foreach` flows a token through the enumeration; a synchronous foreach cannot.
        if (forEach.AwaitKeyword.IsKind(SyntaxKind.None))
            return;

        // Peel any trailing `.WithCancellation(...)` / `.ConfigureAwait(...)` wrappers off the
        // source so the underlying async stream is examined. `source.ConfigureAwait(false)` is a
        // configured cancelable enumerable that still never received a token — that should be
        // flagged, while a chain that already includes `.WithCancellation(...)` should not.
        var source = UnwrapConfiguredEnumerable(
            forEach.Expression,
            context.SemanticModel,
            context.CancellationToken,
            out var hasWithCancellation);
        if (hasWithCancellation)
            return;

        // The (unwrapped) source must be (or implement) IAsyncEnumerable<T>.
        var sourceType = context.SemanticModel.GetTypeInfo(source, context.CancellationToken).Type;
        if (!ImplementsAsyncEnumerable(sourceType))
            return;

        // A token must be in scope to flow it in.
        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            forEach, context.SemanticModel);
        if (tokenParameter == null)
            return;

        // If the producing call already receives a token, it can route it to the iterator's
        // [EnumeratorCancellation] parameter — flagging would be redundant.
        if (source is InvocationExpressionSyntax invocation &&
            CancellationTokenHelpers.HasCancellationTokenArgument(invocation, context.SemanticModel))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty.Add(TokenNameProperty, tokenParameter.Name);
        var diagnostic = Diagnostic.Create(Rule, source.GetLocation(), properties, tokenParameter.Name);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Strips trailing <c>.WithCancellation(...)</c> and <c>.ConfigureAwait(...)</c> calls from an
    /// <c>await foreach</c> source, returning the underlying enumerable expression and reporting
    /// (via <paramref name="hasWithCancellation"/>) whether the framework
    /// <c>.WithCancellation</c> API was present — in which case a token already flows and the loop
    /// must not be flagged. Name-only look-alike methods are not treated as token flow.
    /// </summary>
    private static ExpressionSyntax UnwrapConfiguredEnumerable(
        ExpressionSyntax source,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out bool hasWithCancellation)
    {
        hasWithCancellation = false;

        while (source is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var name = memberAccess.Name.Identifier.Text;
            if (name == "WithCancellation" &&
                IsFrameworkWithCancellation(invocation, semanticModel, cancellationToken))
            {
                hasWithCancellation = true;
                source = memberAccess.Expression;
            }
            else if (name == "ConfigureAwait" &&
                     !CancellationTokenHelpers.HasCancellationTokenArgument(invocation, semanticModel))
            {
                source = memberAccess.Expression;
            }
            else
            {
                break;
            }
        }

        return source;
    }

    private static bool IsFrameworkWithCancellation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var method = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        var definition = method?.ReducedFrom ?? method;

        return definition is
        {
            Name: "WithCancellation",
            ContainingType.Name: "TaskAsyncEnumerableExtensions"
        } &&
        definition.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> is or implements
    /// <c>System.Collections.Generic.IAsyncEnumerable&lt;T&gt;</c>.
    /// </summary>
    private static bool ImplementsAsyncEnumerable(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (IsAsyncEnumerableInterface(type))
            return true;

        return type.AllInterfaces.Any(IsAsyncEnumerableInterface);
    }

    private static bool IsAsyncEnumerableInterface(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { Name: "IAsyncEnumerable", IsGenericType: true } named &&
               named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
    }
}
