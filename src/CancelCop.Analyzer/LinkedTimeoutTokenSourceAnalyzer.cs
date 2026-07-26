using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a timeout <c>CancellationTokenSource</c> that ignores an in-scope parent
/// <c>CancellationToken</c> (not linked via <c>CreateLinkedTokenSource</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC029
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Adding a timeout with <c>new CancellationTokenSource(TimeSpan)</c> or
/// <c>cts.CancelAfter(...)</c> on a stand-alone source silently drops any ambient parent token
/// (request abort, host stopping token, caller cancellation). The operation then continues after
/// the parent is cancelled until the timeout alone fires — a common ASP.NET / worker bug.
/// </para>
/// <para>
/// <b>What it detects:</b>
/// <list type="bullet">
/// <item>Timeout constructors (<c>TimeSpan</c> or <c>int</c> delay) when a token is in scope</item>
/// <item><c>CancelAfter</c> on a local created with parameterless <c>new CancellationTokenSource()</c>
/// when a token is in scope</item>
/// </list>
/// Prefer <c>CreateLinkedTokenSource(parent)</c> + <c>CancelAfter(delay)</c>.
/// </para>
/// <para>
/// Intentional isolated timeouts (must complete even if the parent is cancelled) should keep no
/// parent token in scope, or suppress the diagnostic at the call site.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// public async Task RunAsync(CancellationToken cancellationToken)
/// {
///     using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
///     await DoAsync(cts.Token);
/// }
///
/// // Fixed:
/// public async Task RunAsync(CancellationToken cancellationToken)
/// {
///     using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
///     cts.CancelAfter(TimeSpan.FromSeconds(30));
///     await DoAsync(cts.Token);
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class LinkedTimeoutTokenSourceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC029";

    /// <summary>
    /// Property key used to pass the in-scope token parameter name to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    private const string Category = "Usage";
    private const string CtsTypeName = "CancellationTokenSource";
    private const string SystemThreadingNamespace = "System.Threading";

    private static readonly LocalizableString Title =
        "Timeout CancellationTokenSource should link the in-scope token";
    private static readonly LocalizableString MessageFormat =
        "Timeout CancellationTokenSource ignores in-scope token '{0}'; link it with CreateLinkedTokenSource and CancelAfter";
    private static readonly LocalizableString Description =
        "A timeout CancellationTokenSource that is not linked to the in-scope CancellationToken will not observe parent cancellation (request abort, host stop, caller token).";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeObjectCreation,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression);

        context.RegisterSyntaxNodeAction(AnalyzeCancelAfter, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;

        var methodSymbol = context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol as IMethodSymbol;
        if (methodSymbol is not { MethodKind: MethodKind.Constructor })
            return;

        if (!IsSystemThreadingCancellationTokenSource(methodSymbol.ContainingType))
            return;

        if (!IsTimeoutConstructor(methodSymbol))
            return;

        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            creation, context.SemanticModel);
        if (tokenParameter == null)
            return;

        Report(context, creation.GetLocation(), tokenParameter.Name);
    }

    private static void AnalyzeCancelAfter(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "CancelAfter")
            return;

        var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (methodSymbol == null || !IsSystemThreadingCancellationTokenSource(methodSymbol.ContainingType))
            return;

        // Only instance CancelAfter; static look-alikes are not the framework API.
        if (methodSymbol.IsStatic)
            return;

        var receiver = memberAccess.Expression;
        var receiverSymbol = context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol;
        if (receiverSymbol is not ILocalSymbol local)
            return;

        // CancelAfter on a source already created with a timeout ctor is covered by the creation
        // diagnostic — do not double-report.
        if (!IsParameterlessCancellationTokenSourceCreation(local, context.SemanticModel, context.CancellationToken))
            return;

        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            invocation, context.SemanticModel);
        if (tokenParameter == null)
            return;

        Report(context, memberAccess.Name.GetLocation(), tokenParameter.Name);
    }

    private static void Report(SyntaxNodeAnalysisContext context, Location location, string tokenName)
    {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(TokenNameProperty, tokenName);

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, properties, tokenName));
    }

    private static bool IsTimeoutConstructor(IMethodSymbol constructor)
    {
        if (constructor.Parameters.Length == 0)
            return false;

        // TimeSpan delay, int millisecondsDelay, or TimeSpan + TimeProvider (.NET 8+).
        var first = constructor.Parameters[0].Type;
        return IsTimeSpan(first) || IsInt32(first);
    }

    private static bool IsParameterlessCancellationTokenSourceCreation(
        ILocalSymbol local,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var syntaxRef in local.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(cancellationToken) is not VariableDeclaratorSyntax declarator)
                continue;

            if (declarator.Initializer?.Value is not BaseObjectCreationExpressionSyntax creation)
                return false;

            var ctor = semanticModel.GetSymbolInfo(creation, cancellationToken).Symbol as IMethodSymbol;
            if (ctor is not { MethodKind: MethodKind.Constructor })
                return false;

            if (!IsSystemThreadingCancellationTokenSource(ctor.ContainingType))
                return false;

            // Parameterless new only — timeout ctors are reported on creation; linked factory is
            // an invocation, not an object creation, so it never lands here.
            return ctor.Parameters.Length == 0;
        }

        return false;
    }

    private static bool IsSystemThreadingCancellationTokenSource(ITypeSymbol? type) =>
        type?.Name == CtsTypeName &&
        type.ContainingNamespace?.ToDisplayString() == SystemThreadingNamespace;

    private static bool IsTimeSpan(ITypeSymbol? type) =>
        type?.Name == "TimeSpan" && type.ContainingNamespace?.ToDisplayString() == "System";

    private static bool IsInt32(ITypeSymbol? type) =>
        type?.SpecialType == SpecialType.System_Int32;
}
