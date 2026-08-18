using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>System.Net.HttpListener.GetContext</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC040
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>HttpListener.GetContext</c> parks a thread-pool thread until a request arrives.
/// That wait is unbounded and is not a <c>CancellationToken</c>. <c>GetContextAsync</c>
/// yields the thread.
/// </para>
/// <para>
/// <b>Why this is not CC036–CC039:</b> those rules are symbol-gated to Socket /
/// TcpClient / TcpListener / UdpClient. The HTTP listener is a fifth type —
/// verified empirically against the shipped analyzers.
/// </para>
/// <para>
/// Analyzer-only in this slice: there is no token-taking <c>GetContextAsync</c>
/// overload, so a mechanical rewrite is a follow-up. Report first, rewrite later.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(HttpListener listener, CancellationToken cancellationToken)
/// {
///     listener.GetContext();   // CC040
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingHttpListenerAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC040";

    private static readonly LocalizableString Title =
        "Avoid blocking HttpListener.GetContext in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'HttpListener.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "HttpListener.GetContext parks a thread-pool thread until a request arrives; in async code use GetContextAsync. The async form does not take a CancellationToken.";
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

        context.RegisterCompilationStartAction(start =>
        {
            var listenerType = start.Compilation.GetTypeByMetadataName("System.Net.HttpListener");
            if (listenerType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, listenerType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol listenerType
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var invokedName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            IdentifierNameSyntax identifier => identifier,
            _ => null,
        };
        if (invokedName is null || invokedName.Identifier.Text != "GetContext")
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        var definition = method;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod;

        if (
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, listenerType)
            || definition.Name != "GetContext"
        )
            return;

        if (listenerType.GetMembers("GetContextAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), definition.Name)
        );
    }
}
