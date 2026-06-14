using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking synchronous <c>System.IO</c> call (<c>File</c> read/write/append
/// helpers, or <c>StreamReader.ReadToEnd</c>/<c>ReadLine</c>) inside async code when an async
/// counterpart (<c>&lt;name&gt;Async</c>) exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC028
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Synchronous <c>File</c> helpers such as <c>File.ReadAllText</c> block the calling thread for the
/// whole disk operation. Inside an <c>async</c> method that ties up a thread-pool thread and defeats
/// the point of being async. .NET exposes <c>ReadAllTextAsync</c> / <c>WriteAllTextAsync</c> / … which
/// take a <c>CancellationToken</c>, so the work can both yield the thread and be cancelled. This rounds
/// out the blocking-in-async family alongside CC013 (<c>Thread.Sleep</c>), CC015
/// (<c>Task.Wait</c>/<c>.Result</c>) and CC026 (<c>SemaphoreSlim.Wait</c>).
/// </para>
/// <para>
/// <b>What it detects:</b> a call to one of the well-known blocking <c>System.IO</c> methods
/// (<c>File</c> read/write/append helpers, or <c>StreamReader.ReadToEnd</c>/<c>ReadLine</c>) that has
/// an <c>&lt;name&gt;Async</c> counterpart, made inside an <c>async</c> method, local function, lambda,
/// or anonymous method.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(string path)
/// {
///     var text = File.ReadAllText(path);   // CC028 -> await File.ReadAllTextAsync(path)
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingFileIoAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC028";

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    /// <summary>
    /// The blocking <c>System.IO</c> methods (keyed by declaring type) that have a documented async
    /// counterpart of the form <c>&lt;name&gt;Async</c>.
    /// </summary>
    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> BlockingMethodsByType =
        ImmutableDictionary.CreateRange(new[]
        {
            new KeyValuePair<string, ImmutableHashSet<string>>("File", ImmutableHashSet.Create(
                "ReadAllText", "ReadAllBytes", "ReadAllLines",
                "WriteAllText", "WriteAllBytes", "WriteAllLines",
                "AppendAllText", "AppendAllLines")),
            new KeyValuePair<string, ImmutableHashSet<string>>("StreamReader", ImmutableHashSet.Create(
                "ReadToEnd", "ReadLine")),
        });

    private static readonly LocalizableString Title = "Avoid blocking I/O in async code";
    private static readonly LocalizableString MessageFormat = "Blocking '{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description = "Synchronous System.IO calls block the thread in async code; use the async counterpart, which also accepts a CancellationToken.";
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

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        var containingType = method.ContainingType;
        if (containingType?.ContainingNamespace?.ToDisplayString() != "System.IO")
            return;

        if (!BlockingMethodsByType.TryGetValue(containingType.Name, out var blockingMethods) ||
            !blockingMethods.Contains(methodName))
            return;

        // Only flag when the framework in use actually offers the async counterpart, so the suggestion
        // is always actionable.
        if (!containingType.GetMembers(methodName + "Async").Any())
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            invocation, context.SemanticModel);

        var properties = ImmutableDictionary<string, string?>.Empty;
        if (tokenParameter != null)
            properties = properties.Add(TokenNameProperty, tokenParameter.Name);

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, memberAccess.Name.GetLocation(), properties, methodName));
    }
}
