using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>System.Net.Dns.GetHostAddresses</c>
/// inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC043
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>Dns.GetHostAddresses</c> parks a thread-pool thread on a DNS query.
/// That wait is unbounded and is not a <c>CancellationToken</c>.
/// <c>GetHostAddressesAsync</c> yields the thread; on modern .NET it takes a
/// token.
/// </para>
/// <para>
/// <b>Why this is not CC036–CC042:</b> those rules are symbol-gated to Socket /
/// TcpClient / TcpListener / UdpClient / HttpListener / named-pipe. DNS is a
/// separate type — verified empirically against the shipped analyzers. CC002
/// cannot see it: there is no token overload of the invoked method.
/// </para>
/// <para>
/// Analyzer-only in this slice: a mechanical rewrite is a follow-up. Report first,
/// rewrite later. <c>GetHostEntry</c> is a sibling, deferred.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(string host, CancellationToken cancellationToken)
/// {
///     Dns.GetHostAddresses(host);   // CC043
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingDnsAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC043";

    private static readonly LocalizableString Title =
        "Avoid blocking Dns.GetHostAddresses in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'Dns.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "Dns.GetHostAddresses parks a thread-pool thread on a DNS query; in async code use GetHostAddressesAsync. The token-taking overload is modern .NET only.";
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
        if (invokedName is null || invokedName.Identifier.Text != "GetHostAddresses")
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
            || definition.Name != "GetHostAddresses"
        )
            return;

        if (dnsType.GetMembers("GetHostAddressesAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), definition.Name)
        );
    }
}
