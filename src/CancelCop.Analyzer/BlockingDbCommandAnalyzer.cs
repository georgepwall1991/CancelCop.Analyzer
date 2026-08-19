using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
/// verified empirically. <c>ExecuteNonQuery</c> is CC047.
/// <c>ExecuteScalar</c> is CC048.
/// </para>
/// <para>
/// The fixer rewrites a safe <c>ExecuteReader</c> to
/// <c>await ExecuteReaderAsync</c>, preserving a <c>CommandBehavior</c>
/// argument and flowing an in-scope token when the rewritten call still
/// binds.
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

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    /// <summary>
    /// Property key set when the diagnostic is correct but no safe rewrite exists, so the code fix
    /// must not offer one.
    /// </summary>
    public const string NoFixProperty = "NoFix";

    /// <summary>
    /// Property key used to pass the counterpart's token parameter name when the
    /// original call already uses a named argument, so the fixer can append
    /// <c>cancellationToken: token</c> instead of a trailing positional.
    /// </summary>
    public const string TokenArgumentNameProperty = "TokenArgumentName";

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

        if (
            !TryGetExecuteReaderAsync(
                commandType,
                method.Parameters.Length,
                behaviorType,
                out var withToken,
                out var withoutToken
            ) || (withToken is null && withoutToken is null)
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

        var tokenArgumentName =
            tokenName != null
            && withToken is not null
            && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
                ? FindReachableTokenParameterName(
                    ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
                    invocation.ArgumentList.Arguments.Count,
                    readerType
                ) ?? withToken.Parameters[withToken.Parameters.Length - 1].Name
                : null;

        if (
            tokenName != null
            && (
                withToken is null
                || !ResolvesToTheFrameworkCounterpart(
                    context,
                    invocation,
                    method,
                    tokenName,
                    tokenArgumentName,
                    readerType
                )
            )
        )
        {
            tokenName = null;
            tokenArgumentName = null;
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
            counterpart is not null
            && ResolvesToTheFrameworkCounterpart(
                context,
                invocation,
                method,
                tokenName,
                tokenArgumentName,
                readerType
            )
        )
        {
            if (tokenName != null)
                properties = properties.Add(TokenNameProperty, tokenName);

            if (tokenArgumentName != null)
                properties = properties.Add(TokenArgumentNameProperty, tokenArgumentName);

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invokedName.GetLocation(), properties, method.Name)
            );
            return;
        }

        // A token-taking reader TAP is reachable, but we cannot emit it
        // (no in-scope token, and the parameterless form is unusable).
        // Still report: the blocking call has a real async counterpart.
        if (
            withToken is null
            || !TokenTakingCounterpartIsReachable(context, invocation, method, readerType)
        )
            return;

        if (!properties.ContainsKey(NoFixProperty))
            properties = properties.Add(NoFixProperty, "token-required");

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, method.Name)
        );
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

    private static bool ResolvesToTheFrameworkCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        string? tokenName,
        string? tokenArgumentName,
        INamedTypeSymbol? readerType
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "ExecuteReaderAsync",
            tokenName,
            tokenArgumentName
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
            return IsUsableAsyncCounterpart(bound, readerType);
        }

        var emittedArity = invocation.ArgumentList.Arguments.Count + (tokenName is null ? 0 : 1);
        return ReachesCounterpart(
            ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
            emittedArity,
            readerType
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

    /// <summary>
    /// <c>ExecuteReaderAsync()</c> and <c>ExecuteReaderAsync(CancellationToken)</c>
    /// are not virtual — providers hide them with <c>new Task&lt;SqlDataReader&gt;</c>.
    /// Accept a TAP method that still returns a <c>DbDataReader</c> (or
    /// subclass). <c>new Task&lt;int&gt; ExecuteReaderAsync</c> is not a reader
    /// API even though it is awaitable.
    /// </summary>
    private static bool IsUsableAsyncCounterpart(IMethodSymbol? bound, INamedTypeSymbol? readerType)
    {
        if (
            bound
            is not {
                IsStatic: false,
                Name: "ExecuteReaderAsync",
                ReturnType: INamedTypeSymbol
                {
                    IsGenericType: true,
                    Name: "Task" or "ValueTask",
                    TypeArguments.Length: 1,
                } task,
            }
        )
            return false;

        if (task.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks")
            return false;

        return readerType is not null && IsOrInherits(task.TypeArguments[0], readerType);
    }

    /// <summary>
    /// Finds the token parameter name on the TAP <c>ExecuteReaderAsync</c>
    /// the rewrite will bind to. Provider hiders may rename it from
    /// <c>cancellationToken</c>.
    /// </summary>
    private static string? FindReachableTokenParameterName(
        ITypeSymbol? receiverType,
        int originalArgCount,
        INamedTypeSymbol? readerType
    )
    {
        var arity = originalArgCount + 1;
        var seen = new List<IMethodSymbol>();

        for (var current = receiverType; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers("ExecuteReaderAsync").OfType<IMethodSymbol>())
            {
                if (seen.Any(s => SameSignature(s, member)))
                    continue;

                seen.Add(member);

                var required = member.Parameters.Count(p => !p.IsOptional);
                if (arity < required || arity > member.Parameters.Length)
                    continue;

                if (!IsUsableAsyncCounterpart(member, readerType))
                    continue;

                var last = member.Parameters[member.Parameters.Length - 1];
                return CancellationTokenHelpers.IsCancellationToken(last.Type) ? last.Name : null;
            }
        }

        return null;
    }

    private static bool TokenTakingCounterpartIsReachable(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        INamedTypeSymbol? readerType
    )
    {
        return ReachesCounterpart(
            ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
            invocation.ArgumentList.Arguments.Count + 1,
            readerType
        );
    }

    /// <summary>
    /// Walks the receiver type for an applicable TAP <c>ExecuteReaderAsync</c>.
    /// A same-signature <c>new</c> hider blocks the framework method;
    /// an unrelated extra overload (e.g. <c>ExecuteReaderAsync(int)</c>)
    /// does not, so scanning continues.
    /// </summary>
    private static bool ReachesCounterpart(
        ITypeSymbol? receiverType,
        int arity,
        INamedTypeSymbol? readerType
    )
    {
        var seen = new List<IMethodSymbol>();

        for (var current = receiverType; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers("ExecuteReaderAsync").OfType<IMethodSymbol>())
            {
                if (seen.Any(s => SameSignature(s, member)))
                    continue;

                seen.Add(member);

                var required = member.Parameters.Count(p => !p.IsOptional);
                if (arity < required || arity > member.Parameters.Length)
                    continue;

                if (IsUsableAsyncCounterpart(member, readerType))
                    return true;
            }
        }

        return false;
    }

    private static bool SameSignature(IMethodSymbol left, IMethodSymbol right)
    {
        if (left.Parameters.Length != right.Parameters.Length)
            return false;

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (left.Parameters[i].RefKind != right.Parameters[i].RefKind)
                return false;

            if (
                !SymbolEqualityComparer.Default.Equals(
                    left.Parameters[i].Type,
                    right.Parameters[i].Type
                )
            )
                return false;
        }

        return true;
    }

    private static bool TryGetExecuteReaderAsync(
        INamedTypeSymbol commandType,
        int originalArgCount,
        INamedTypeSymbol? behaviorType,
        out IMethodSymbol? withToken,
        out IMethodSymbol? withoutToken
    )
    {
        withToken = null;
        withoutToken = null;

        foreach (var member in commandType.GetMembers("ExecuteReaderAsync"))
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

            if (originalArgCount == 0)
            {
                if (
                    candidate.Parameters.Length == 1
                    && CancellationTokenHelpers.IsCancellationToken(candidate.Parameters[0].Type)
                )
                    withToken = candidate;
                else if (candidate.Parameters.Length == 0)
                    withoutToken = candidate;
            }
            else if (originalArgCount == 1 && behaviorType is not null)
            {
                if (
                    candidate.Parameters.Length == 2
                    && SymbolEqualityComparer.Default.Equals(
                        candidate.Parameters[0].Type,
                        behaviorType
                    )
                    && CancellationTokenHelpers.IsCancellationToken(candidate.Parameters[1].Type)
                )
                    withToken = candidate;
                else if (
                    candidate.Parameters.Length == 1
                    && SymbolEqualityComparer.Default.Equals(
                        candidate.Parameters[0].Type,
                        behaviorType
                    )
                )
                    withoutToken = candidate;
            }
        }

        return withToken is not null || withoutToken is not null;
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
