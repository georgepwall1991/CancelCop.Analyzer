using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>System.Net.Sockets.Socket</c> operation inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC036
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// A socket call blocks until the network responds — or until a TCP timeout that can run into
/// minutes. Inside async code that parks a thread-pool thread on a remote party's behaviour, which
/// is the least predictable thing a server waits on. <c>Accept</c> and <c>Connect</c> are worse
/// still: they can block indefinitely with no data to wait for.
/// </para>
/// <para>
/// <b>How this differs from CC028:</b> CC028 covers blocking <c>System.IO</c> calls, including every
/// <c>Stream</c> — so a <c>NetworkStream</c> is already handled. It offers a code fix, which it can
/// only do safely because it requires the async counterpart to be <i>signature-compatible</i>: the
/// same parameters, optionally plus a token. Socket's async APIs are not shaped that way —
/// <c>Receive(byte[])</c> pairs with <c>ReceiveAsync(Memory&lt;byte&gt;, CancellationToken)</c>, and
/// <c>Accept()</c> with <c>AcceptAsync(CancellationToken)</c> returning a different type. Loosening
/// CC028's matching to reach them would give up the property that makes its rewrites safe, so this
/// is a separate rule — and analyzer-only, because there is no mechanical rewrite.
/// </para>
/// <para>
/// <b>Conservative by design:</b> only the blocking members that genuinely have an async
/// counterpart are listed, matched through the override chain to <c>Socket</c> itself, and only
/// inside async code. A synchronous method, or a synchronous lambda inside an async one, stays
/// quiet.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task ServeAsync(Socket listener)
/// {
///     var client = listener.Accept();   // CC036: blocks a pooled thread until someone connects
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingSocketIoAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC036";

    /// <summary>
    /// The blocking <c>Socket</c> members that have an asynchronous counterpart.
    /// </summary>
    private static readonly ImmutableHashSet<string> BlockingMembers = ImmutableHashSet.Create(
        "Receive",
        "ReceiveFrom",
        "ReceiveMessageFrom",
        "Send",
        "SendTo",
        "SendFile",
        "Accept",
        "Connect",
        "Disconnect"
    );

    private static readonly LocalizableString Title = "Avoid blocking socket calls in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'Socket.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "A blocking socket call parks a thread-pool thread until the network responds; in async code use the Async counterpart, which also accepts a CancellationToken.";
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

        // Resolve Socket once per compilation and compare symbols, so a consumer's own type named
        // System.Net.Sockets.Socket is not mistaken for the framework one.
        context.RegisterCompilationStartAction(start =>
        {
            var socketType = start.Compilation.GetTypeByMetadataName("System.Net.Sockets.Socket");
            if (socketType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, socketType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol socketType
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
        if (invokedName is null || !BlockingMembers.Contains(invokedName.Identifier.Text))
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        // Walk to the original definition so a subclass's override still resolves to Socket.
        var definition = method;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod;

        if (
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, socketType)
            || !BlockingMembers.Contains(definition.Name)
        )
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), definition.Name)
        );
    }
}
