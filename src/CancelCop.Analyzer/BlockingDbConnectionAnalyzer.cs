using System.Collections.Immutable;
using System.Linq;
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
/// The fixer rewrites a safe <c>Open()</c> to <c>await OpenAsync</c>,
/// flowing an in-scope token when the rewritten call still binds.
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

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    /// <summary>
    /// Property key set when the diagnostic is correct but no safe rewrite exists, so the code fix
    /// must not offer one.
    /// </summary>
    public const string NoFixProperty = "NoFix";

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

        if (
            !TryGetOpenAsync(connectionType, out var withToken, out var withoutToken)
            || (withToken is null && withoutToken is null)
        )
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        if (CancellationTokenHelpers.AwaitInsertionIsUnsafe(context.SemanticModel, invocation))
            properties = properties.Add(NoFixProperty, "await-unsafe");

        var tokenName = CancellationTokenHelpers
            .FindEnclosingCancellationToken(invocation, context.SemanticModel)
            ?.ExpressionText;

        if (
            tokenName != null
            && (
                withToken is null
                || !ResolvesToTheFrameworkCounterpart(
                    context,
                    invocation,
                    method,
                    tokenName,
                    withToken
                )
            )
        )
        {
            tokenName = null;
        }

        var counterpart =
            tokenName != null
                ? withToken
                : withoutToken
                    ?? (
                        withToken is not null && withToken.Parameters.All(p => p.IsOptional)
                            ? withToken
                            : null
                    );

        if (
            counterpart is null
            || !ResolvesToTheFrameworkCounterpart(
                context,
                invocation,
                method,
                tokenName,
                counterpart
            )
        )
            return;

        if (tokenName != null)
            properties = properties.Add(TokenNameProperty, tokenName);

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
        );
    }

    private static bool ResolvesToTheFrameworkCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        string? tokenName,
        IMethodSymbol asyncCounterpart
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "OpenAsync",
            tokenName
        );

        if (speculative != null)
        {
            var bound =
                context
                    .SemanticModel.GetSpeculativeSymbolInfo(
                        invocation.SpanStart,
                        speculative,
                        SpeculativeBindingOption.BindAsExpression
                    )
                    .Symbol as IMethodSymbol;
            return IsSameOrOverrideOf(bound, asyncCounterpart);
        }

        return ReachesCounterpart(
            ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
            tokenName is null ? 0 : 1,
            asyncCounterpart
        );
    }

    private static ITypeSymbol? ReceiverTypeOf(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation
    )
    {
        var receiver = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            MemberBindingExpressionSyntax => invocation
                .Ancestors()
                .OfType<ConditionalAccessExpressionSyntax>()
                .FirstOrDefault()
                ?.Expression,
            _ => null,
        };

        return receiver is null
            ? null
            : context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type;
    }

    private static bool IsSameOrOverrideOf(IMethodSymbol? bound, IMethodSymbol expected)
    {
        for (var current = bound; current is not null; current = current.OverriddenMethod)
        {
            if (
                SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    expected.OriginalDefinition
                )
            )
                return true;
        }

        return false;
    }

    private static bool ReachesCounterpart(
        ITypeSymbol? receiverType,
        int arity,
        IMethodSymbol asyncCounterpart
    )
    {
        for (var current = receiverType; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers("OpenAsync").OfType<IMethodSymbol>())
            {
                var required = member.Parameters.Count(p => !p.IsOptional);
                if (arity < required || arity > member.Parameters.Length)
                    continue;

                if (IsSameOrOverrideOf(member, asyncCounterpart))
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetOpenAsync(
        INamedTypeSymbol connectionType,
        out IMethodSymbol? withToken,
        out IMethodSymbol? withoutToken
    )
    {
        withToken = null;
        withoutToken = null;

        foreach (var member in connectionType.GetMembers("OpenAsync"))
        {
            if (
                member
                is not IMethodSymbol
                {
                    IsStatic: false,
                    DeclaredAccessibility: Accessibility.Public,
                    ReturnType.Name: "Task",
                } candidate
            )
                continue;

            if (
                candidate.ReturnType.ContainingNamespace?.ToDisplayString()
                != "System.Threading.Tasks"
            )
                continue;

            if (
                candidate.Parameters.Length == 1
                && CancellationTokenHelpers.IsCancellationToken(candidate.Parameters[0].Type)
            )
                withToken = candidate;
            else if (candidate.Parameters.Length == 0)
                withoutToken = candidate;
        }

        return withToken is not null || withoutToken is not null;
    }
}
