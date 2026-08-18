using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking
/// <c>System.Data.Common.DbConnection.Open</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC045
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>DbConnection.Open</c> parks a thread-pool thread on a database
/// handshake. That wait is not a <c>CancellationToken</c>.
/// <c>OpenAsync</c> yields the thread and accepts a token (since .NET Framework 4.5).
/// Concrete providers (SqlConnection, Npgsql, …) match through the override
/// chain.
/// </para>
/// <para>
/// <b>Why this is not CC003:</b> CC003 is symbol-gated to EF Core query
/// methods. ADO.NET Open produced zero diagnostics from every shipped rule —
/// verified empirically. <c>DbCommand.ExecuteReader</c> is CC046.
/// </para>
/// <para>
/// Analyzer-only in this slice: a mechanical rewrite is a follow-up.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(DbConnection connection, CancellationToken cancellationToken)
/// {
///     connection.Open();   // CC045
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingDbConnectionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC045";

    private static readonly LocalizableString Title =
        "Avoid blocking DbConnection.Open in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'DbConnection.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "DbConnection.Open parks a thread-pool thread on a database handshake; in async code use OpenAsync. OpenAsync has accepted a CancellationToken since .NET Framework 4.5.";
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
            var connectionType = start.Compilation.GetTypeByMetadataName(
                "System.Data.Common.DbConnection"
            );
            if (connectionType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, connectionType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol connectionType
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
        if (invokedName is null || invokedName.Identifier.Text != "Open")
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
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, connectionType)
            || definition.Name != "Open"
        )
            return;

        if (connectionType.GetMembers("OpenAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), definition.Name)
        );
    }
}
