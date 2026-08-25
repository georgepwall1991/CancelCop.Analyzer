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
/// <c>System.Net.NetworkInformation.Ping.Send</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC050
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>Ping.Send</c> parks a thread-pool thread until the echo reply arrives
/// (or a timeout elapses). That wait is not a <c>CancellationToken</c>.
/// <c>SendPingAsync</c> yields the thread; on modern .NET it takes a token.
/// </para>
/// <para>
/// The TAP counterpart is <c>SendPingAsync</c> — verified by name, because
/// <c>Ping.SendAsync</c> is the event-based EAP overload set and must never be
/// treated as the async counterpart. The analyzer requires
/// <c>SendPingAsync</c> to exist on the type before reporting; the token-taking
/// <c>SendPingAsync</c> overloads are modern .NET only (the rewrite falls back
/// to the tokenless form when no token is in scope).
/// </para>
/// <para>
/// The fixer rewrites a safe <c>Send</c> to <c>await SendPingAsync</c>,
/// preserving the original arguments and flowing an in-scope token when the
/// rewritten call still binds. Null-conditional statements hoist to an
/// <c>is not null</c> guard; await-forbidden contexts (lock bodies, unsafe)
/// and a bare <c>Send(...)</c> inside a <c>SendPingAsync</c> override are
/// reported without a fix.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(Ping ping, CancellationToken cancellationToken)
/// {
///     ping.Send("example.org");   // CC050
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingPingAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC050";

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
        "Avoid blocking Ping.Send in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'Ping.{0}' in async code; use 'SendPingAsync'";
    private static readonly LocalizableString Description =
        "Ping.Send parks a thread-pool thread until the echo reply arrives; in async code use SendPingAsync. Ping's event-based SendAsync is not the TAP counterpart. The token-taking SendPingAsync overload is modern .NET only.";
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
            var pingType = start.Compilation.GetTypeByMetadataName(
                "System.Net.NetworkInformation.Ping"
            );
            if (pingType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, pingType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol pingType
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
        if (invokedName is null || invokedName.Identifier.Text != "Send")
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
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, pingType)
            || definition.Name != "Send"
        )
            return;

        // The TAP counterpart is SendPingAsync — NOT the event-based SendAsync.
        if (pingType.GetMembers("SendPingAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        if (CancellationTokenHelpers.AwaitInsertionIsUnsafe(context.SemanticModel, invocation))
            properties = properties.Add(NoFixProperty, "await-unsafe");

        if (
            !properties.ContainsKey(NoFixProperty)
            && IsInsideSendPingAsync(context, invocation, pingType)
        )
            properties = properties.Add(NoFixProperty, "self-async");

        var tokenName = CancellationTokenHelpers
            .FindEnclosingCancellationToken(invocation, context.SemanticModel)
            ?.ExpressionText;

        var tokenArgumentName =
            tokenName != null && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
                ? FindTokenParameterName(pingType)
                : null;

        if (
            ResolvesToUsableCounterpart(
                context,
                invocation,
                pingType,
                tokenName,
                tokenArgumentName
            )
            || ResolvesToUsableCounterpart(context, invocation, pingType, null, null)
        )
        {
            // Token-taking rebind failed but the tokenless form binds: drop the token.
            if (
                !ResolvesToUsableCounterpart(
                    context,
                    invocation,
                    pingType,
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
                FindTokenParameterName(pingType)
            );

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
        );
    }

    private static bool IsInsideSendPingAsync(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol pingType
    )
    {
        // Only an implicit-this call (a bare `Send(...)` without a receiver)
        // can retarget the enclosing SendPingAsync itself and recurse.
        if (invocation.Expression is not IdentifierNameSyntax)
            return false;

        var enclosing =
            context.SemanticModel.GetEnclosingSymbol(
                invocation.SpanStart,
                context.CancellationToken
            ) as IMethodSymbol;

        while (
            enclosing is { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction }
        )
            enclosing = enclosing.ContainingSymbol as IMethodSymbol;

        return enclosing is not null
            && enclosing.Name == "SendPingAsync"
            && DerivesFromOrEquals(enclosing.ContainingType, pingType)
            && IsTaskLike(enclosing.ReturnType);
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

    private static string? FindTokenParameterName(INamedTypeSymbol pingType)
    {
        for (var current = pingType; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers("SendPingAsync").OfType<IMethodSymbol>())
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
        INamedTypeSymbol pingType,
        string? tokenName,
        string? tokenArgumentName
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "SendPingAsync",
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
            && bound.Name == "SendPingAsync"
            && IsTaskLike(bound.ReturnType)
            && SymbolEqualityComparer.Default.Equals(
                bound.OriginalDefinition.ContainingType,
                pingType
            )
            && bound.Parameters.Count(p =>
                !CancellationTokenHelpers.IsCancellationToken(p.Type)
            ) == invocation.ArgumentList.Arguments.Count;
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
