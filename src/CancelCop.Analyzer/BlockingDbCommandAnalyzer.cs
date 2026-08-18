using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking
/// <c>System.Data.Common.DbCommand.ExecuteReader</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC046
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>DbCommand.ExecuteReader</c> parks a thread-pool thread on a database
/// query. That wait is not a <c>CancellationToken</c>.
/// <c>ExecuteReaderAsync</c> yields the thread and accepts a token (since
/// .NET Framework 4.5). Concrete providers (SqlCommand, NpgsqlCommand, …)
/// typically hide <c>ExecuteReader</c> with <c>new</c> for a covariant
/// reader, so the rule matches inheritance plus the framework shape —
/// not only <c>OverriddenMethod</c>, which is empty because the method
/// is not virtual. Custom helpers, generic helpers, and statics stay quiet.
/// </para>
/// <para>
/// <b>Why this is not CC003 or CC045:</b> CC003 is symbol-gated to EF Core
/// query methods. CC045 is <c>DbConnection.Open</c>. ADO.NET
/// <c>ExecuteReader</c> produced zero diagnostics from every shipped rule —
/// verified empirically. <c>ExecuteNonQuery</c> / <c>ExecuteScalar</c> are
/// siblings, deferred.
/// </para>
/// <para>
/// Analyzer-only in this slice: a mechanical rewrite is a follow-up.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(DbCommand command, CancellationToken cancellationToken)
/// {
///     command.ExecuteReader();   // CC046
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingDbCommandAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC046";

    private static readonly LocalizableString Title =
        "Avoid blocking DbCommand.ExecuteReader in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'DbCommand.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "DbCommand.ExecuteReader parks a thread-pool thread on a database query; in async code use ExecuteReaderAsync. ExecuteReaderAsync has accepted a CancellationToken since .NET Framework 4.5.";
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

            var readerType = start.Compilation.GetTypeByMetadataName(
                "System.Data.Common.DbDataReader"
            );
            var behaviorType = start.Compilation.GetTypeByMetadataName(
                "System.Data.CommandBehavior"
            );

            start.RegisterSyntaxNodeAction(
                nodeContext =>
                    AnalyzeInvocation(nodeContext, commandType, readerType, behaviorType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol commandType,
        INamedTypeSymbol? readerType,
        INamedTypeSymbol? behaviorType
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
        if (invokedName is null || invokedName.Identifier.Text != "ExecuteReader")
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        if (!IsFrameworkExecuteReader(method, commandType, readerType, behaviorType))
            return;

        if (commandType.GetMembers("ExecuteReaderAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invokedName.GetLocation(), method.Name));
    }

    /// <summary>
    /// <c>ExecuteReader</c> is not virtual. Providers hide the framework
    /// overloads with <c>new</c> for a covariant reader, so
    /// <c>OverriddenMethod</c> is empty. Match those hiders by shape:
    /// instance, returns <c>DbDataReader</c> (or subclass), and either
    /// parameterless or a single <c>CommandBehavior</c>. Custom helpers,
    /// generic helpers, and statics stay quiet.
    /// </summary>
    private static bool IsFrameworkExecuteReader(
        IMethodSymbol method,
        INamedTypeSymbol commandType,
        INamedTypeSymbol? readerType,
        INamedTypeSymbol? behaviorType
    )
    {
        if (method.IsStatic || method.Arity != 0 || readerType is null)
            return false;

        if (!IsOrInherits(method.ContainingType, commandType))
            return false;

        if (!IsOrInherits(method.ReturnType, readerType))
            return false;

        if (method.Parameters.Length == 0)
            return true;

        return method.Parameters.Length == 1
            && behaviorType is not null
            && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, behaviorType);
    }

    private static bool IsOrInherits(ITypeSymbol? type, INamedTypeSymbol expected)
    {
        for (
            var current = type as INamedTypeSymbol;
            current is not null;
            current = current.BaseType
        )
        {
            if (SymbolEqualityComparer.Default.Equals(current, expected))
                return true;
        }

        return false;
    }
}
