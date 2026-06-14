using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects EF Core async query calls that do not propagate an in-scope
/// <c>CancellationToken</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC003
/// </para>
/// <para>
/// <b>Why this matters:</b> EF Core async operations (<c>ToListAsync</c>, <c>FirstOrDefaultAsync</c>,
/// <c>SaveChangesAsync</c>, …) each accept a <c>CancellationToken</c> that cancels the underlying
/// database round-trip. Omitting it leaves a query running on the server after the caller has given
/// up, holding a connection and burning database time.
/// </para>
/// <para>
/// <b>What it detects:</b> a call to a method in the <c>Microsoft.EntityFrameworkCore</c> namespace
/// that has a token-accepting overload, where a token is in scope (method, local function, lambda,
/// constructor, or primary constructor) and the call does not already pass one. Calls inside an
/// expression tree are excluded — they are translated, not executed. Shares the
/// <see cref="CancellationTokenHelpers.ReportIfTokenNotPropagated"/> pipeline with CC002/CC004.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task&lt;User&gt; GetAsync(int id, CancellationToken cancellationToken)
///     =&gt; await _db.Users.FirstOrDefaultAsync(u =&gt; u.Id == id);   // CC003: pass cancellationToken
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EFCoreAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CC003";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "EF Core async method should receive CancellationToken";
    private static readonly LocalizableString MessageFormat = "EF Core method '{0}' should receive CancellationToken parameter '{1}'";
    private static readonly LocalizableString Description = "EF Core async methods should receive a CancellationToken to allow database query cancellation.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

    // Common EF Core async extension methods
    private static readonly ImmutableHashSet<string> EFCoreAsyncMethods = ImmutableHashSet.Create(
        "FirstOrDefaultAsync", "FirstAsync",
        "SingleOrDefaultAsync", "SingleAsync",
        "ToListAsync", "ToArrayAsync", "ToDictionaryAsync",
        "AnyAsync", "AllAsync", "CountAsync", "LongCountAsync",
        "ForEachAsync", "SumAsync", "AverageAsync", "MinAsync", "MaxAsync",
        "SaveChangesAsync", "LoadAsync"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Get the method symbol for the invocation
        var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol == null)
            return;

        // Check if this is an EF Core async method
        if (!EFCoreAsyncMethods.Contains(methodSymbol.Name))
            return;

        // Check if the method is from Microsoft.EntityFrameworkCore namespace
        var containingNamespace = methodSymbol.ContainingNamespace?.ToDisplayString();
        if (containingNamespace == null || !containingNamespace.StartsWith("Microsoft.EntityFrameworkCore"))
            return;

        // Shared tail: token-in-scope, not-already-passed, executable-code, and overload checks.
        CancellationTokenHelpers.ReportIfTokenNotPropagated(context, invocation, methodSymbol, Rule);
    }
}
