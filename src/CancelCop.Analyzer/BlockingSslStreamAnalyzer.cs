using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking
/// <c>System.Net.Security.SslStream.AuthenticateAsClient</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC051
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>SslStream.AuthenticateAsClient</c> parks a thread-pool thread for the
/// entire TLS handshake — network round-trips, certificate validation, and
/// cipher negotiation. In async code use <c>AuthenticateAsClientAsync</c>,
/// which yields the thread and accepts a <c>CancellationToken</c> (on its
/// <see cref="System.Net.Security.SslClientAuthenticationOptions"/> overload).
/// </para>
/// <para>
/// The TAP counterpart is <c>AuthenticateAsClientAsync</c>, verified by name.
/// The analyzer requires it to exist on the type before reporting; only the
/// options-based arity takes a token, so the rewrite flows an in-scope token
/// only when the rewritten call still binds and falls back to the tokenless
/// string arities otherwise. SslStream is not sealed, so overrides are
/// resolved by walking the <c>.OverriddenMethod</c> chain back to
/// <c>SslStream</c>.
/// </para>
/// <para>
/// The fixer rewrites a safe <c>AuthenticateAsClient</c> to
/// <c>await AuthenticateAsClientAsync</c>, preserving the original arguments
/// and flowing an in-scope token when the rewritten call still binds.
/// Null-conditional statements hoist to an <c>is not null</c> guard;
/// await-forbidden contexts (lock bodies, unsafe) and a bare
/// <c>AuthenticateAsClient(...)</c> inside an
/// <c>AuthenticateAsClientAsync</c> override are reported without a fix.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(SslStream stream, CancellationToken cancellationToken)
/// {
///     stream.AuthenticateAsClient("example.org");   // CC051
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingSslStreamAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC051";

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
        "Avoid blocking SslStream.AuthenticateAsClient in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'SslStream.{0}' in async code; use 'AuthenticateAsClientAsync'";
    private static readonly LocalizableString Description =
        "SslStream.AuthenticateAsClient parks a thread-pool thread for the whole TLS handshake; in async code use AuthenticateAsClientAsync. Only the SslClientAuthenticationOptions arity accepts a CancellationToken.";
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
            var sslStreamType = start.Compilation.GetTypeByMetadataName(
                "System.Net.Security.SslStream"
            );
            if (sslStreamType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, sslStreamType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol sslStreamType
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
            || invokedName.Identifier.Text != "AuthenticateAsClient"
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
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, sslStreamType)
            || definition.Name != "AuthenticateAsClient"
        )
            return;

        // The TAP counterpart is AuthenticateAsClientAsync — NOT the APM
        // BeginAuthenticateAsClient/EndAuthenticateAsClient pair.
        if (sslStreamType.GetMembers("AuthenticateAsClientAsync").IsEmpty)
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
            && IsInsideAuthenticateAsClientAsync(context, invocation, sslStreamType)
        )
            properties = properties.Add(NoFixProperty, "self-async");

        var tokenName = CancellationTokenHelpers
            .FindEnclosingCancellationToken(invocation, context.SemanticModel)
            ?.ExpressionText;

        var tokenArgumentName =
            tokenName != null && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
                ? FindTokenParameterName(sslStreamType)
                : null;

        if (
            ResolvesToUsableCounterpart(
                context,
                invocation,
                sslStreamType,
                tokenName,
                tokenArgumentName
            )
            || ResolvesToUsableCounterpart(context, invocation, sslStreamType, null, null)
        )
        {
            // Token-taking rebind failed but the tokenless form binds: drop the token.
            if (
                !ResolvesToUsableCounterpart(
                    context,
                    invocation,
                    sslStreamType,
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
        // offer a named-token candidate it re-validates by speculative binding.
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
                FindTokenParameterName(sslStreamType)
            );

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
        );
    }

    private static bool IsInsideAuthenticateAsClientAsync(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol sslStreamType
    )
    {
        // A bare `AuthenticateAsClient(...)` — or one on a receiver that is
        // provably `this` (`this`, `base`, or a local assigned from this) inside an
        // AuthenticateAsClientAsync member retargets the enclosing call itself and
        // recurses when the fix virtually dispatches. Withhold those.
        if (!ReceiverCouldDispatchToEnclosing(invocation))
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
            && enclosing.Name == "AuthenticateAsClientAsync"
            && DerivesFromOrEquals(enclosing.ContainingType, sslStreamType)
            && IsTaskLike(enclosing.ReturnType);
    }

    private static bool ReceiverCouldDispatchToEnclosing(
        InvocationExpressionSyntax invocation
    )
    {
        // A bare `AuthenticateAsClient(...)` IS an implicit-this call.
        if (invocation.Expression is IdentifierNameSyntax)
            return true;

        ExpressionSyntax? receiver;
        if (invocation.Expression is MemberBindingExpressionSyntax)
        {
            // A `?.` spine surfaces as a member binding; the receiver is the
            // conditional access's operation (`self?.AuthenticateAsClient(...)`).
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
                return false;
        }
        else if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = memberAccess.Expression;
        }
        else
        {
            return false;
        }

        while (receiver is ParenthesizedExpressionSyntax parenthesized)
            receiver = parenthesized.Expression;

        // Only receivers that are PROVABLY fresh instances (`new SslStream(...)`,
        // a factory call) cannot dispatch to `this`. Anything else — this, base,
        // locals, parameters, fields, properties — could alias the enclosing
        // instance and recurse after the rewrite, so it is withheld.
        return receiver
            is not (ObjectCreationExpressionSyntax or InvocationExpressionSyntax);
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

    private static string? FindTokenParameterName(INamedTypeSymbol sslStreamType)
    {
        for (var current = sslStreamType; current != null; current = current.BaseType)
        {
            foreach (
                var member in current
                    .GetMembers("AuthenticateAsClientAsync")
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
        INamedTypeSymbol sslStreamType,
        string? tokenName,
        string? tokenArgumentName
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "AuthenticateAsClientAsync",
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
            && bound.Name == "AuthenticateAsClientAsync"
            && IsTaskLike(bound.ReturnType)
            && ResolvesOnFrameworkStream(bound, sslStreamType)
            && bound.Parameters.Count(p =>
                !CancellationTokenHelpers.IsCancellationToken(p.Type)
            ) == invocation.ArgumentList.Arguments.Count;
    }

    private static bool ResolvesOnFrameworkStream(
        IMethodSymbol bound,
        INamedTypeSymbol sslStreamType
    )
    {
        // Walk overrides so a legitimate override of the framework TAP member keeps
        // its framework lineage; a same-named `new` hider has no override chain and
        // must declare on SslStream itself to pass.
        var definition = bound.OriginalDefinition;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod.OriginalDefinition;

        return SymbolEqualityComparer.Default.Equals(definition.ContainingType, sslStreamType);
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
