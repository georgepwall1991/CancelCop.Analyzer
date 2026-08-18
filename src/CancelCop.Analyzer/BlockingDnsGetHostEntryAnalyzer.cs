using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>System.Net.Dns.GetHostEntry</c>
/// inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC044
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>Dns.GetHostEntry</c> parks a thread-pool thread on a DNS query,
/// including reverse lookup of a numeric IP. That wait is not a
/// <c>CancellationToken</c>. <c>GetHostEntryAsync</c> yields the thread; on
/// modern .NET the string overloads take a token.
/// </para>
/// <para>
/// <b>Why this is not CC043:</b> CC043 is symbol-gated to
/// <c>GetHostAddresses</c>. GetHostEntry is a sibling — verified empirically.
/// A compile-time IP literal is <b>not</b> exempt: unlike GetHostAddresses,
/// GetHostEntry still does reverse DNS for a numeric address.
/// </para>
/// <para>
/// Analyzer-only in this slice: a mechanical rewrite is a follow-up.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(string host, CancellationToken cancellationToken)
/// {
///     Dns.GetHostEntry(host);   // CC044
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingDnsGetHostEntryAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC044";

    private static readonly LocalizableString Title =
        "Avoid blocking Dns.GetHostEntry in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'Dns.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "Dns.GetHostEntry parks a thread-pool thread on a DNS query, including reverse lookup of a numeric IP; in async code use GetHostEntryAsync. The token-taking string overload is modern .NET only.";
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
            var dnsType = start.Compilation.GetTypeByMetadataName("System.Net.Dns");
            if (dnsType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, dnsType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol dnsType
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
        if (invokedName is null || invokedName.Identifier.Text != "GetHostEntry")
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
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, dnsType)
            || definition.Name != "GetHostEntry"
        )
            return;

        if (dnsType.GetMembers("GetHostEntryAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), definition.Name)
        );
    }
}
