using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking
/// <c>System.Data.Common.DbCommand.ExecuteScalar</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC048
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>DbCommand.ExecuteScalar</c> parks a thread-pool thread on a
/// single-value query. That wait is not a <c>CancellationToken</c>.
/// <c>ExecuteScalarAsync</c> yields the thread and accepts a token (since
/// .NET Framework 4.5). The method is abstract, so overrides match;
/// <c>new</c> hiders still match by inheritance plus the framework shape
/// (including a more-derived return such as <c>string</c>). Custom
/// helpers, generic helpers, statics, <c>void</c> hiders, and
/// <c>Task</c>/<c>ValueTask</c> hiders stay quiet.
/// </para>
/// <para>
/// <b>Why this is not CC003, CC045, CC046, or CC047:</b> CC003 is EF Core.
/// CC045 is <c>DbConnection.Open</c>. CC046 is <c>ExecuteReader</c>. CC047
/// is <c>ExecuteNonQuery</c>. ADO.NET <c>ExecuteScalar</c> produced zero
/// diagnostics from every shipped rule — verified empirically.
/// </para>
/// <para>
/// Analyzer-only in this slice: a mechanical rewrite is a follow-up.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
/// {
///     command.ExecuteScalar();   // CC048
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingDbScalarAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC048";

    private static readonly LocalizableString Title =
        "Avoid blocking DbCommand.ExecuteScalar in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'DbCommand.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "DbCommand.ExecuteScalar parks a thread-pool thread on a single-value query; in async code use ExecuteScalarAsync. ExecuteScalarAsync has accepted a CancellationToken since .NET Framework 4.5.";
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
            var commandType = start.Compilation.GetTypeByMetadataName(
                "System.Data.Common.DbCommand"
            );
            if (commandType is null)
                return;

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, commandType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol commandType
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
        if (invokedName is null || invokedName.Identifier.Text != "ExecuteScalar")
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        if (!IsFrameworkExecuteScalar(method, commandType))
            return;

        if (commandType.GetMembers("ExecuteScalarAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invokedName.GetLocation(), method.Name));
    }

    /// <summary>
    /// Match the framework <c>ExecuteScalar()</c> shape: instance, arity 0,
    /// non-<c>void</c> return, no parameters, declared on <c>DbCommand</c>
    /// or a subclass. Overrides, <c>new object</c> hiders, and more-derived
    /// returns report; <c>void</c> hiders, <c>Task</c>/<c>ValueTask</c>
    /// hiders, custom helpers, and generics stay quiet.
    /// </summary>
    private static bool IsFrameworkExecuteScalar(IMethodSymbol method, INamedTypeSymbol commandType)
    {
        if (method.IsStatic || method.Arity != 0)
            return false;

        if (!IsOrInherits(method.ContainingType, commandType))
            return false;

        if (
            method.ReturnType.SpecialType == SpecialType.System_Void
            || IsTaskLike(method.ReturnType)
        )
            return false;

        return method.Parameters.Length == 0;
    }

    private static bool IsTaskLike(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            return false;

        var definition = named.OriginalDefinition;
        if (definition.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks")
            return false;

        return definition.Name is "Task" or "ValueTask";
    }

    private static bool IsOrInherits(INamedTypeSymbol? type, INamedTypeSymbol expected)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expected))
                return true;
        }

        return false;
    }
}
