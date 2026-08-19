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
/// rule — verified empirically. <c>ExecuteScalar</c> is CC048.
/// </para>
/// <para>
/// The fixer rewrites a safe <c>ExecuteNonQuery()</c> to
/// <c>await ExecuteNonQueryAsync</c>, flowing an in-scope token when the
/// rewritten call still binds to a <c>Task&lt;int&gt;</c> TAP method.
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

        if (
            !TryGetExecuteNonQueryAsync(commandType, out var withToken, out var withoutToken)
            || (withToken is null && withoutToken is null)
        )
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        if (CancellationTokenHelpers.AwaitInsertionIsUnsafe(context.SemanticModel, invocation))
            properties = properties.Add(NoFixProperty, "await-unsafe");

        // Rewriting ExecuteNonQuery() inside ExecuteNonQueryAsync would recurse
        // through the public TAP entry point. Report, but do not offer a fix.
        if (
            !properties.ContainsKey(NoFixProperty)
            && IsInsideExecuteNonQueryAsync(context, invocation, commandType)
        )
            properties = properties.Add(NoFixProperty, "self-async");

        var tokenName = CancellationTokenHelpers
            .FindEnclosingCancellationToken(invocation, context.SemanticModel)
            ?.ExpressionText;

        if (
            tokenName != null
            && (withToken is null || !ResolvesToUsableCounterpart(context, invocation, tokenName))
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

        if (counterpart is not null && ResolvesToUsableCounterpart(context, invocation, tokenName))
        {
            if (tokenName != null)
                properties = properties.Add(TokenNameProperty, tokenName);

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invokedName.GetLocation(), properties, method.Name)
            );
            return;
        }

        if (
            withToken is null
            || !ReachesCounterpart(ReceiverTypeOf(context, invocation) ?? method.ReceiverType, 1)
        )
            return;

        if (!properties.ContainsKey(NoFixProperty))
            properties = properties.Add(NoFixProperty, "token-required");

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, method.Name)
        );
    }

    /// <summary>
    /// Match the framework <c>ExecuteNonQuery()</c> shape: instance, arity
    /// 0, returns <c>int</c>, no parameters, declared on
    /// <c>DbCommand</c> or a subclass. Overrides and <c>new</c> hiders
    /// report; custom helpers and generics stay quiet.
    /// </summary>
    private static bool IsInsideExecuteNonQueryAsync(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol commandType
    )
    {
        var enclosing =
            context.SemanticModel.GetEnclosingSymbol(
                invocation.SpanStart,
                context.CancellationToken
            ) as IMethodSymbol;

        while (
            enclosing is { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction }
        )
            enclosing = enclosing.ContainingSymbol as IMethodSymbol;

        if (
            enclosing is not { Name: "ExecuteNonQueryAsync" }
            || !IsOrInherits(enclosing.ContainingType, commandType)
            || !IsUsableAsyncCounterpart(enclosing)
        )
            return false;

        // Only the recursive this/implicit-this call is unsafe.
        // other.ExecuteNonQuery() and base.ExecuteNonQuery() still rewrite.
        return invocation.Expression switch
        {
            IdentifierNameSyntax => true,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } => true,
            _ => false,
        };
    }

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

    private static bool ResolvesToUsableCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string? tokenName
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            "ExecuteNonQueryAsync",
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
            return IsUsableAsyncCounterpart(bound);
        }

        return ReachesCounterpart(ReceiverTypeOf(context, invocation), tokenName is null ? 0 : 1);
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

    private static bool IsUsableAsyncCounterpart(IMethodSymbol? bound)
    {
        if (
            bound
            is not {
                IsStatic: false,
                Name: "ExecuteNonQueryAsync",
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

        if (task.TypeArguments[0].SpecialType != SpecialType.System_Int32)
            return false;

        if (bound.Parameters.Length == 0)
            return true;

        return bound.Parameters.Length == 1
            && CancellationTokenHelpers.IsCancellationToken(bound.Parameters[0].Type);
    }

    private static bool ReachesCounterpart(ITypeSymbol? receiverType, int arity)
    {
        var seen = new List<IMethodSymbol>();

        for (var current = receiverType; current != null; current = current.BaseType)
        {
            foreach (
                var member in current.GetMembers("ExecuteNonQueryAsync").OfType<IMethodSymbol>()
            )
            {
                if (seen.Any(s => SameSignature(s, member)))
                    continue;

                seen.Add(member);

                var required = member.Parameters.Count(p => !p.IsOptional);
                if (arity < required || arity > member.Parameters.Length)
                    continue;

                if (IsUsableAsyncCounterpart(member))
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

    private static bool TryGetExecuteNonQueryAsync(
        INamedTypeSymbol commandType,
        out IMethodSymbol? withToken,
        out IMethodSymbol? withoutToken
    )
    {
        withToken = null;
        withoutToken = null;

        foreach (var member in commandType.GetMembers("ExecuteNonQueryAsync"))
        {
            if (!IsUsableAsyncCounterpart(member as IMethodSymbol))
                continue;

            var candidate = (IMethodSymbol)member;
            if (candidate.DeclaredAccessibility != Accessibility.Public)
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
