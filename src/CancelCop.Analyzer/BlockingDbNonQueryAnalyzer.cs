using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking
/// <c>System.Data.Common.DbCommand.ExecuteNonQuery</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC047
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>DbCommand.ExecuteNonQuery</c> parks a thread-pool thread on a
/// command that does not return rows. That wait is not a
/// <c>CancellationToken</c>. <c>ExecuteNonQueryAsync</c> yields the thread
/// and accepts a token (since .NET Framework 4.5). The method is abstract,
/// so overrides match; <c>new</c> hiders still match by inheritance plus
/// the framework shape. Custom helpers, generic helpers, and statics stay
/// quiet.
/// </para>
/// <para>
/// <b>Why this is not CC003, CC045, or CC046:</b> CC003 is EF Core. CC045
/// is <c>DbConnection.Open</c>. CC046 is <c>ExecuteReader</c>. ADO.NET
/// <c>ExecuteNonQuery</c> produced zero diagnostics from every shipped
/// rule — verified empirically. <c>ExecuteScalar</c> is a sibling,
/// deferred.
/// </para>
/// <para>
/// Analyzer-only in this slice: a mechanical rewrite is a follow-up.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
/// {
///     command.ExecuteNonQuery();   // CC047
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingDbNonQueryAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC047";

    private static readonly LocalizableString Title =
        "Avoid blocking DbCommand.ExecuteNonQuery in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'DbCommand.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "DbCommand.ExecuteNonQuery parks a thread-pool thread on a command that does not return rows; in async code use ExecuteNonQueryAsync. ExecuteNonQueryAsync has accepted a CancellationToken since .NET Framework 4.5.";
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
        if (invokedName is null || invokedName.Identifier.Text != "ExecuteNonQuery")
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        if (!IsFrameworkExecuteNonQuery(method, commandType))
            return;

        if (commandType.GetMembers("ExecuteNonQueryAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invokedName.GetLocation(), method.Name));
    }

    /// <summary>
    /// Match the framework <c>ExecuteNonQuery()</c> shape: instance, arity
    /// 0, returns <c>int</c>, no parameters, declared on
    /// <c>DbCommand</c> or a subclass. Overrides and <c>new</c> hiders
    /// report; custom helpers and generics stay quiet.
    /// </summary>
    private static bool IsFrameworkExecuteNonQuery(
        IMethodSymbol method,
        INamedTypeSymbol commandType
    )
    {
        if (method.IsStatic || method.Arity != 0)
            return false;

        if (!IsOrInherits(method.ContainingType, commandType))
            return false;

        if (method.ReturnType.SpecialType != SpecialType.System_Int32)
            return false;

        return method.Parameters.Length == 0;
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
