using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>System.Net.WebRequest.GetResponse</c>
/// inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC052
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>WebRequest.GetResponse</c> parks a thread-pool thread for the entire
/// request/response round trip — connection establishment, the network
/// round-trips, and body download. In async code use <c>GetResponseAsync</c>,
/// which yields the thread. The TAP counterpart is parameterless and accepts
/// no <see cref="System.Threading.CancellationToken"/>, so the rewrite is
/// always tokenless; cancellation requires replacing the legacy
/// <c>WebRequest</c> stack (e.g. with <c>HttpClient</c>).
/// </para>
/// <para>
/// <c>GetResponse</c> is virtual on <c>WebRequest</c> — and so is
/// <c>GetResponseAsync</c> — so overrides are resolved by walking the
/// <c>.OverriddenMethod</c> chain back to <c>WebRequest</c>; derived types
/// such as <c>HttpWebRequest</c>/<c>FileWebRequest</c> match through it.
/// </para>
/// <para>
/// The fixer rewrites a safe <c>GetResponse</c> to
/// <c>await GetResponseAsync</c>, preserving the original arguments.
/// Null-conditional statements hoist to an <c>is not null</c> guard;
/// await-forbidden contexts (lock bodies, unsafe) and a bare
/// <c>GetResponse(...)</c> inside a <c>GetResponseAsync</c> override are
/// reported without a fix.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(WebRequest request, CancellationToken cancellationToken)
/// {
///     request.GetResponse();   // CC052
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingWebRequestAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC052";

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
        "Avoid blocking WebRequest.GetResponse in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'WebRequest.{0}' in async code; use 'GetResponseAsync'";
    private static readonly LocalizableString Description =
        "WebRequest.GetResponse parks a thread-pool thread for the whole request/response round trip; in async code use GetResponseAsync. The TAP counterpart takes no CancellationToken, so the rewrite stays tokenless.";
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
            var webRequestType = start.Compilation.GetTypeByMetadataName(
                "System.Net.WebRequest"
            );
            if (webRequestType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, webRequestType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol webRequestType
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
        if (
            invokedName is null
            || invokedName.Identifier.Text != "GetResponse"
        )
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
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, webRequestType)
            || definition.Name != "GetResponse"
        )
            return;

        // The TAP counterpart is GetResponseAsync — NOT the APM
        // BeginGetResponse/EndGetResponse pair.
        if (webRequestType.GetMembers("GetResponseAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        if (
            CancellationTokenHelpers.AwaitInsertionIsUnsafe(
                context.SemanticModel,
                invocation
            )
        )
            properties = properties.Add(NoFixProperty, "await-unsafe");

        if (
            !properties.ContainsKey(NoFixProperty)
            && IsInsideGetResponseAsync(context, invocation, webRequestType)
        )
            properties = properties.Add(NoFixProperty, "self-async");

        var tokenName = CancellationTokenHelpers
            .FindEnclosingCancellationToken(invocation, context.SemanticModel)
            ?.ExpressionText;

        var tokenArgumentName =
            tokenName != null && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
                ? FindTokenParameterName(webRequestType)
                : null;

        if (
            ResolvesToUsableCounterpart(
                context,
                invocation,
                webRequestType,
                tokenName,
                tokenArgumentName
            )
            || ResolvesToUsableCounterpart(context, invocation, webRequestType, null, null)
        )
        {
            // Token-taking rebind failed but the tokenless form binds: drop the token.
            if (
                !ResolvesToUsableCounterpart(
                    context,
                    invocation,
                    webRequestType,
                    tokenName,
                    tokenArgumentName
                )
            )
            {
                tokenName = null;
                tokenArgumentName = null;
            }

            if (tokenName != null)
                properties = properties.Add(TokenNameProperty, tokenName);

            if (tokenArgumentName != null)
                properties = properties.Add(TokenArgumentNameProperty, tokenArgumentName);

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
            );
            return;
        }

        // No speculative rebind is possible (conditional-access spine or an
        // unusable shape), but the call IS blocking: report without a rewrite.
        // The in-scope token still rides along so the fixer's statement hoist can
        // offer a candidate it re-validates by speculative binding.
        if (!properties.ContainsKey(NoFixProperty))
            properties = properties.Add(
                NoFixProperty,
                CancellationTokenHelpers.IsWhenNotNullOfConditionalAccess(invocation)
                    ? "conditional-access"
                    : "no-safe-rewrite"
            );

        var hoistTokenName =
            tokenName
            ?? CancellationTokenHelpers
                .FindEnclosingCancellationToken(invocation, context.SemanticModel)
                ?.ExpressionText;
        if (hoistTokenName != null && !properties.ContainsKey(TokenNameProperty))
            properties = properties.Add(TokenNameProperty, hoistTokenName);
        if (
            hoistTokenName != null
            && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
            && !properties.ContainsKey(TokenArgumentNameProperty)
        )
            properties = properties.Add(
                TokenArgumentNameProperty,
                FindTokenParameterName(webRequestType)
            );

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
        );
    }

    private static bool IsInsideGetResponseAsync(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol webRequestType
    )
    {
        // A bare `GetResponse(...)` — or one on a receiver that is
        // provably `this` (`this`, `base`, or a local assigned from this) inside a
        // GetResponseAsync member retargets the enclosing call itself and
        // recurses when the fix virtually dispatches. Withhold those.
        if (ReceiverIsProvablyFresh(invocation))
            return false;

        var enclosing =
            context
                .SemanticModel.GetEnclosingSymbol(
                    invocation.SpanStart,
                    context.CancellationToken
                )
                as IMethodSymbol;

        while (
            enclosing is { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction }
        )
            enclosing = enclosing.ContainingSymbol as IMethodSymbol;

        return enclosing is not null
            && enclosing.Name == "GetResponseAsync"
            && DerivesFromOrEquals(enclosing.ContainingType, webRequestType)
            && IsTaskLike(enclosing.ReturnType);
    }

    private static bool ReceiverIsProvablyFresh(InvocationExpressionSyntax invocation)
    {
        // A bare `GetResponse(...)` IS an implicit-this call — never fresh.
        if (invocation.Expression is IdentifierNameSyntax)
            return false;

        ExpressionSyntax? receiver;
        if (invocation.Expression is MemberBindingExpressionSyntax)
        {
            // A `?.` spine surfaces as a member binding; the receiver is the
            // conditional access's operation (`self?.GetResponse(...)`).
            receiver = null;
            for (
                var current = invocation.Parent;
                current is not null;
                current = current.Parent
            )
            {
                if (
                    current is ConditionalAccessExpressionSyntax conditional
                    && ReferenceEquals(invocation, conditional.WhenNotNull)
                )
                {
                    receiver = conditional.Expression;
                    break;
                }
            }

            if (receiver is null)
                return true;
        }
        else if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = memberAccess.Expression;
        }
        else
        {
            return true;
        }

        while (receiver is ParenthesizedExpressionSyntax parenthesized)
            receiver = parenthesized.Expression;
        // Only `new WebRequest(...)` is PROVABLY fresh: a derived construction
        // (`new Client(...)`) may be the enclosing instance, and an invocation result
        // may be a factory that returns `this`. Anything else — this, base, locals,
        // parameters, fields, properties — could alias the enclosing instance and
        // recurse after the rewrite, so it is withheld.
        return receiver
            is ObjectCreationExpressionSyntax
            {
                Type: IdentifierNameSyntax { Identifier.Text: "WebRequest" },
            };
    }

    private static bool DerivesFromOrEquals(ITypeSymbol? type, INamedTypeSymbol baseType)
    {
        while (type != null)
        {
            if (SymbolEqualityComparer.Default.Equals(type, baseType))
                return true;
            type = type.BaseType;
        }

        return false;
    }

    private static string? FindTokenParameterName(INamedTypeSymbol webRequestType)
    {
        for (var current = webRequestType; current != null; current = current.BaseType)
        {
            foreach (
                var member in current
                    .GetMembers("GetResponseAsync")
                    .OfType<IMethodSymbol>()
            )
            {
                if (member.Parameters.IsEmpty)
                    continue;

                var last = member.Parameters[member.Parameters.Length - 1];
                if (CancellationTokenHelpers.IsCancellationToken(last.Type))
                    return last.Name;
            }
        }

        return "cancellationToken";
    }

    private static bool ResolvesToUsableCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol webRequestType,
        string? tokenName,
        string? tokenArgumentName
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "GetResponseAsync",
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
            && !bound.IsStatic
            && bound.Name == "GetResponseAsync"
            && IsTaskLike(bound.ReturnType)
            && ResolvesOnFrameworkRequest(bound, webRequestType)
            && bound.Parameters.Count(p =>
                !CancellationTokenHelpers.IsCancellationToken(p.Type)
            ) == invocation.ArgumentList.Arguments.Count;
    }

    private static bool ResolvesOnFrameworkRequest(
        IMethodSymbol bound,
        INamedTypeSymbol webRequestType
    )
    {
        // Walk overrides so a legitimate override of the framework TAP member keeps
        // its framework lineage; a same-named `new` hider has no override chain and
        // must declare on WebRequest itself to pass.
        var definition = bound.OriginalDefinition;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod.OriginalDefinition;

        return SymbolEqualityComparer.Default.Equals(definition.ContainingType, webRequestType);
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
