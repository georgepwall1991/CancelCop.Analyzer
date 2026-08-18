using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking
/// <c>System.IO.Pipes.NamedPipeClientStream.Connect</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC042
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>NamedPipeClientStream.Connect</c> parks a thread-pool thread until the
/// server accepts (or a timeout elapses). That wait is not a
/// <c>CancellationToken</c>. <c>ConnectAsync</c> yields the thread; on modern
/// .NET it takes a token.
/// </para>
/// <para>
/// <b>Why this is not CC041:</b> CC041 is symbol-gated to
/// <c>NamedPipeServerStream.WaitForConnection</c>. The client connect is a
/// sibling type — verified empirically against the shipped analyzers.
/// </para>
/// <para>
/// Analyzer-only in this slice: a mechanical rewrite is a follow-up. Report first,
/// rewrite later.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
/// {
///     client.Connect();   // CC042
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingNamedPipeClientAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC042";

    private static readonly LocalizableString Title =
        "Avoid blocking NamedPipeClientStream.Connect in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'NamedPipeClientStream.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "NamedPipeClientStream.Connect parks a thread-pool thread until the server accepts; in async code use ConnectAsync. The token-taking overload is modern .NET only.";
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
            var clientType = start.Compilation.GetTypeByMetadataName(
                "System.IO.Pipes.NamedPipeClientStream"
            );
            if (clientType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, clientType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol clientType
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
        if (invokedName is null || invokedName.Identifier.Text != "Connect")
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
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, clientType)
            || definition.Name != "Connect"
        )
            return;

        if (clientType.GetMembers("ConnectAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), definition.Name)
        );
    }
}
