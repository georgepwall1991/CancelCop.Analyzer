using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking
/// <c>System.IO.Pipes.NamedPipeServerStream.WaitForConnection</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC041
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>NamedPipeServerStream.WaitForConnection</c> parks a thread-pool thread until a
/// client connects. That wait is unbounded and is not a <c>CancellationToken</c>.
/// <c>WaitForConnectionAsync</c> yields the thread; on modern .NET it takes a token.
/// </para>
/// <para>
/// <b>Why this is not CC028 / CC036–CC040:</b> CC028 maps File/Stream
/// <c>Read</c>/<c>Write</c>/<c>CopyTo</c>/<c>Flush</c> only.
/// CC036–CC040 are Socket / TcpClient / TcpListener / UdpClient / HttpListener.
/// The named-pipe server is a sixth type — verified empirically against the
/// shipped analyzers.
/// </para>
/// <para>
/// Analyzer-only in this slice: a mechanical rewrite is a follow-up. Report first,
/// rewrite later.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
/// {
///     server.WaitForConnection();   // CC041
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingNamedPipeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC041";

    private static readonly LocalizableString Title =
        "Avoid blocking NamedPipeServerStream.WaitForConnection in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'NamedPipeServerStream.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "NamedPipeServerStream.WaitForConnection parks a thread-pool thread until a client connects; in async code use WaitForConnectionAsync. The token-taking overload is modern .NET only.";
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
            var serverType = start.Compilation.GetTypeByMetadataName(
                "System.IO.Pipes.NamedPipeServerStream"
            );
            if (serverType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, serverType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol serverType
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
        if (invokedName is null || invokedName.Identifier.Text != "WaitForConnection")
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
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, serverType)
            || definition.Name != "WaitForConnection"
        )
            return;

        if (serverType.GetMembers("WaitForConnectionAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), definition.Name)
        );
    }
}
