using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>System.Threading.Thread.Join</c>
/// inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC053
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>Thread.Join</c> parks the calling thread-pool thread until the joined
/// thread terminates — an unbounded wait in async code. Await the task that
/// represents the work instead of joining a raw thread.
/// </para>
/// <para>
/// Verified against the net9/net10 reference packs:
/// <c>System.Threading.Thread</c> declares only
/// <c>Join()</c>, <c>Join(int)</c>, and <c>Join(TimeSpan)</c> — none of them
/// virtual — and declares no TAP counterpart at all, so CC053 is
/// analyzer-only by design: every diagnostic is reported without a rewrite.
/// <c>Thread</c> is also sealed on current .NET, so no user-derived
/// override or hider can participate.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(Thread thread, CancellationToken cancellationToken)
/// {
///     thread.Join();   // CC053
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingThreadJoinAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC053";

    private static readonly LocalizableString Title =
        "Avoid blocking Thread.Join in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'Thread.{0}' in async code; await the task representing the work instead";
    private static readonly LocalizableString Description =
        "Thread.Join parks the calling thread-pool thread until the joined thread terminates; in async code await the task that represents the work. Thread declares no TAP JoinAsync counterpart on current .NET, so no rewrite is offered.";
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
            var threadType = start.Compilation.GetTypeByMetadataName(
                "System.Threading.Thread"
            );
            if (threadType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, threadType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol threadType
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
            || invokedName.Identifier.Text != "Join"
        )
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        // Thread.Join overloads are not virtual in the framework; the walk is
        // kept for symmetry with the family rules and resolves any derived
        // override back to its framework lineage.
        var definition = method;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod;

        if (
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, threadType)
            || definition.Name != "Join"
        )
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        // A provably-zero timeout is an immediate probe, not a wait — parity with
        // CC031's exclusion for the same shape.
        if (
            CancellationTokenHelpers.HasProvablyZeroTimeout(
                invocation,
                context.SemanticModel,
                context.CancellationToken
            )
        )
        {
            return;
        }

        // Analyzer-only by design: Thread declares no JoinAsync on any shipped .NET,
        // so there is nothing to rewrite toward and no code-fix provider is exported.
        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                invokedName.GetLocation(),
                ImmutableDictionary<string, string?>.Empty,
                definition.Name
            )
        );
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
