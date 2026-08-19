using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
/// The fixer rewrites a safe <c>GetHostAddresses</c> to
/// <c>await GetHostAddressesAsync</c>, flowing an in-scope token when
/// the rewritten call still binds. The <c>AddressFamily</c> TAP has an
/// optional token, so a tokenless rewrite still compiles.
/// An identifier-form <c>using static</c> rewrite is withheld when
/// speculative bind would land on a same-named helper rather than
/// <c>System.Net.Dns</c>. <c>Dns</c> is a static type.
/// <c>GetHostEntry</c> is CC044. A
/// compile-time constant string that <c>IPAddress.TryParse</c> accepts
/// is a parse, not a query, and stays quiet.
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

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    /// <summary>
    /// Property key set when the diagnostic is correct but no safe rewrite exists.
    /// </summary>
    public const string NoFixProperty = "NoFix";

    /// <summary>
    /// Property key for the TAP token parameter name when the original call
    /// already uses named arguments.
    /// </summary>
    public const string TokenArgumentNameProperty = "TokenArgumentName";

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

        var properties = ImmutableDictionary<string, string?>.Empty;

        if (CancellationTokenHelpers.AwaitInsertionIsUnsafe(context.SemanticModel, invocation))
            properties = properties.Add(NoFixProperty, "await-unsafe");

        var tokenName = CancellationTokenHelpers
            .FindEnclosingCancellationToken(invocation, context.SemanticModel)
            ?.ExpressionText;

        var tokenArgumentName =
            tokenName != null && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
                ? FindTokenParameterName(dnsType, definition, context)
                : null;

        if (
            tokenName != null
            && !ResolvesToUsableCounterpart(
                context,
                invocation,
                dnsType,
                definition,
                tokenName,
                tokenArgumentName
            )
        )
        {
            tokenName = null;
            tokenArgumentName = null;
        }

        if (
            ResolvesToUsableCounterpart(
                context,
                invocation,
                dnsType,
                definition,
                tokenName,
                tokenArgumentName
            )
        )
        {
            if (tokenName != null)
                properties = properties.Add(TokenNameProperty, tokenName);

            if (tokenArgumentName != null)
                properties = properties.Add(TokenArgumentNameProperty, tokenArgumentName);

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
            );
            return;
        }

        if (!ReachesCounterpart(dnsType, definition, context))
            return;

        if (!properties.ContainsKey(NoFixProperty))
            properties = properties.Add(NoFixProperty, "no-safe-rewrite");

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
        );
    }

    private static string? FindTokenParameterName(
        INamedTypeSymbol dnsType,
        IMethodSymbol getHostAddresses,
        SyntaxNodeAnalysisContext context
    )
    {
        foreach (var member in ReachableGetHostAddressesAsync(dnsType, context))
        {
            if (!MatchesGetHostAddressesShape(member, getHostAddresses))
                continue;

            if (member.Parameters.IsEmpty)
                continue;

            var last = member.Parameters[member.Parameters.Length - 1];
            if (CancellationTokenHelpers.IsCancellationToken(last.Type))
                return last.Name;
        }

        return "cancellationToken";
    }

    private static bool ResolvesToUsableCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol dnsType,
        IMethodSymbol getHostAddresses,
        string? tokenName,
        string? tokenArgumentName
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "GetHostAddressesAsync",
            tokenName,
            tokenArgumentName
        );
        if (speculative is null)
            return false;

        var bound =
            context
                .SemanticModel.GetSpeculativeSymbolInfo(
                    invocation.SpanStart,
                    speculative,
                    SpeculativeBindingOption.BindAsExpression
                )
                .Symbol as IMethodSymbol;
        return bound is not null
            && IsUsableAsyncCounterpart(bound, dnsType)
            && MatchesGetHostAddressesShape(bound, getHostAddresses);
    }

    private static bool ReachesCounterpart(
        INamedTypeSymbol dnsType,
        IMethodSymbol getHostAddresses,
        SyntaxNodeAnalysisContext context
    )
    {
        foreach (var member in ReachableGetHostAddressesAsync(dnsType, context))
        {
            if (
                IsUsableAsyncCounterpart(member, dnsType)
                && MatchesGetHostAddressesShape(member, getHostAddresses)
            )
                return true;
        }

        return false;
    }

    private static IEnumerable<IMethodSymbol> ReachableGetHostAddressesAsync(
        INamedTypeSymbol dnsType,
        SyntaxNodeAnalysisContext context
    )
    {
        var enclosing =
            context.ContainingSymbol
            ?? context.SemanticModel.GetEnclosingSymbol(
                context.Node.SpanStart,
                context.CancellationToken
            );
        var compilation = context.SemanticModel.Compilation;
        ISymbol within =
            enclosing as INamedTypeSymbol
            ?? enclosing?.ContainingType
            ?? (ISymbol)compilation.Assembly;

        foreach (var member in dnsType.GetMembers("GetHostAddressesAsync").OfType<IMethodSymbol>())
        {
            if (within is not null && !compilation.IsSymbolAccessibleWithin(member, within))
                continue;

            yield return member;
        }
    }

    private static bool IsUsableAsyncCounterpart(IMethodSymbol? bound, INamedTypeSymbol dnsType)
    {
        if (bound is not { IsStatic: true, Name: "GetHostAddressesAsync" })
            return false;

        if (!SymbolEqualityComparer.Default.Equals(bound.ContainingType, dnsType))
            return false;

        if (!IsTaskLike(bound.ReturnType))
            return false;

        if (bound.Parameters.IsEmpty)
            return false;

        var last = bound.Parameters[bound.Parameters.Length - 1];
        if (CancellationTokenHelpers.IsCancellationToken(last.Type))
            return true;

        // Tokenless string TAP: GetHostAddressesAsync(string). There is no
        // tokenless GetHostAddressesAsync(string, AddressFamily).
        return bound.Parameters.Length == 1
            && bound.Parameters[0].Type.SpecialType == SpecialType.System_String;
    }

    private static bool MatchesGetHostAddressesShape(IMethodSymbol tap, IMethodSymbol sync)
    {
        var tapArgs = tap
            .Parameters.Where(p => !CancellationTokenHelpers.IsCancellationToken(p.Type))
            .ToArray();
        if (tapArgs.Length != sync.Parameters.Length)
            return false;

        for (var i = 0; i < tapArgs.Length; i++)
        {
            if (tapArgs[i].RefKind != sync.Parameters[i].RefKind)
                return false;

            if (!SymbolEqualityComparer.Default.Equals(tapArgs[i].Type, sync.Parameters[i].Type))
                return false;
        }

        return true;
    }

    private static bool IsTaskLike(ITypeSymbol type)
    {
        for (
            var current = type as INamedTypeSymbol;
            current is not null;
            current = current.BaseType
        )
        {
            var definition = current.OriginalDefinition;
            if (definition.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks")
                continue;

            if (definition.Name is "Task" or "ValueTask")
                return true;
        }

        return false;
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
