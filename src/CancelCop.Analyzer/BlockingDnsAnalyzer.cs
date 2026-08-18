using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
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
/// rewrite later. <c>GetHostEntry</c> is a sibling, deferred. A compile-time
/// constant string that <c>IPAddress.TryParse</c> accepts is a parse, not a
/// query, and stays quiet.
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
        "Dns.GetHostAddresses parks a thread-pool thread on a DNS query; in async code use GetHostAddressesAsync. The token-taking overload is modern .NET only. A compile-time constant IP literal is a parse, not a query, and is not reported.";
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

        if (IsProvablyIpLiteral(invocation, context))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), definition.Name)
        );
    }

    /// <summary>
    /// True when the first argument is a compile-time constant IPv4 or IPv6
    /// literal. IPv4 is checked host-independently (four decimal octets, no
    /// leading zeros) so .NET Framework vs modern parser differences cannot
    /// silence a real query. IPv6 uses <see cref="IPAddress.TryParse(string, out IPAddress)"/>.
    /// Non-const locals, <c>localhost</c>, and named-reordered calls stay reported.
    /// </summary>
    private static bool IsProvablyIpLiteral(
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context
    )
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return false;

        var first = invocation.ArgumentList.Arguments[0];
        if (first.NameColon is { } named && named.Name.Identifier.Text != "hostNameOrAddress")
            return false;

        var constant = context.SemanticModel.GetConstantValue(
            first.Expression,
            context.CancellationToken
        );
        return constant.HasValue
            && constant.Value is string value
            && IsHostIndependentIpLiteral(value);
    }

    /// <summary>
    /// Host-independent IP shape: dotted IPv4 must be four 0–255 octets with
    /// no leading zeros (modern .NET rejects <c>010.0.0.1</c> as an IP and
    /// treats it as a DNS name). Other strings must parse as IPv6.
    /// </summary>
    private static bool IsHostIndependentIpLiteral(string value)
    {
        if (LooksLikeDottedIPv4(value))
            return IsStrictIPv4(value);

        return IPAddress.TryParse(value, out var parsed)
            && parsed.AddressFamily == AddressFamily.InterNetworkV6;
    }

    private static bool LooksLikeDottedIPv4(string value)
    {
        var dots = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '.')
                dots++;
            else if (c < '0' || c > '9')
                return false;
        }

        return dots == 3;
    }

    private static bool IsStrictIPv4(string value)
    {
        var start = 0;
        var octets = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i != value.Length && value[i] != '.')
                continue;

            var length = i - start;
            if (length == 0 || length > 3)
                return false;
            if (length > 1 && value[start] == '0')
                return false;

            var n = 0;
            for (var j = start; j < i; j++)
                n = (n * 10) + (value[j] - '0');
            if (n > 255)
                return false;

            octets++;
            start = i + 1;
        }

        return octets == 4;
    }
}
