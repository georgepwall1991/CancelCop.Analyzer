using System.Collections.Generic;
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

        // Thread declares no JoinAsync on any shipped .NET (verified against
        // the net9/net10 ref packs), so unlike the sibling rules there is no
        // counterpart-existence gate: the rule still reports the blocking
        // call. The speculative rebind below stays so a future framework
        // JoinAsync enables the rewrite automatically.

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

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



    private static bool ResolvesOnFrameworkThread(
        IMethodSymbol bound,
        INamedTypeSymbol threadType
    )
    {
        // Walk overrides so a legitimate override of the framework TAP member keeps
        // its framework lineage; a same-named `new` hider has no override chain and
        // must declare on Thread itself to pass.
        var definition = bound.OriginalDefinition;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod.OriginalDefinition;

        return SymbolEqualityComparer.Default.Equals(definition.ContainingType, threadType);
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
