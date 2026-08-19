using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
/// The fixer rewrites a safe <c>GetHostEntry</c> to
/// <c>await GetHostEntryAsync</c>, flowing an in-scope token when the
/// rewritten call still binds to <c>System.Net.Dns</c>. The
/// <c>IPAddress</c> TAP is tokenless, so that rewrite never invents a
/// token. The <c>AddressFamily</c> TAP has an optional token. An
/// identifier-form <c>using static</c> rewrite is withheld when bind
/// would land on a same-named helper.
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
        IMethodSymbol getHostEntry,
        SyntaxNodeAnalysisContext context
    )
    {
        foreach (var member in ReachableGetHostEntryAsync(dnsType, context))
        {
            if (!MatchesGetHostEntryShape(member, getHostEntry))
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
        IMethodSymbol getHostEntry,
        string? tokenName,
        string? tokenArgumentName
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "GetHostEntryAsync",
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
            && MatchesGetHostEntryShape(bound, getHostEntry);
    }

    private static bool ReachesCounterpart(
        INamedTypeSymbol dnsType,
        IMethodSymbol getHostEntry,
        SyntaxNodeAnalysisContext context
    )
    {
        foreach (var member in ReachableGetHostEntryAsync(dnsType, context))
        {
            if (
                IsUsableAsyncCounterpart(member, dnsType)
                && MatchesGetHostEntryShape(member, getHostEntry)
            )
                return true;
        }

        return false;
    }

    private static IEnumerable<IMethodSymbol> ReachableGetHostEntryAsync(
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

        foreach (var member in dnsType.GetMembers("GetHostEntryAsync").OfType<IMethodSymbol>())
        {
            if (within is not null && !compilation.IsSymbolAccessibleWithin(member, within))
                continue;

            yield return member;
        }
    }

    private static bool IsUsableAsyncCounterpart(IMethodSymbol? bound, INamedTypeSymbol dnsType)
    {
        if (bound is not { IsStatic: true, Name: "GetHostEntryAsync" })
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

        // Tokenless TAP: GetHostEntryAsync(string) or GetHostEntryAsync(IPAddress).
        return bound.Parameters.Length == 1 && IsTokenlessTapArgument(bound.Parameters[0].Type);
    }

    private static bool IsTokenlessTapArgument(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_String
        || (
            type.Name == "IPAddress" && type.ContainingNamespace?.ToDisplayString() == "System.Net"
        );

    private static bool MatchesGetHostEntryShape(IMethodSymbol tap, IMethodSymbol sync)
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
}
