using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a non-async method returning a task whose receiver is a <c>using</c>-scoped
/// resource — the resource is disposed before the returned task completes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC027
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// A <c>using</c> declaration disposes its resource when the method returns. If the method returns a
/// task produced by calling that resource — <c>return resource.DoAsync();</c> — the resource is
/// disposed while the task is still running, so the caller awaits an operation on a disposed object
/// (often an <c>ObjectDisposedException</c>). Making the method <c>async</c> and <c>await</c>ing the
/// call keeps the resource alive until completion.
/// </para>
/// <para>
/// <b>What it detects:</b> a non-<c>async</c> method or local function returning
/// <c>Task</c>/<c>Task&lt;T&gt;</c>/<c>ValueTask</c> where a <c>return</c> expression is a call whose
/// left-most receiver is a <c>using</c>-declared local. Only the receiver case is flagged (high
/// confidence); a resource read synchronously into a completed task — e.g.
/// <c>Task.FromResult(resource.Value)</c> — is not.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public Task&lt;byte[]&gt; ReadAsync(string path)
/// {
///     using var stream = File.OpenRead(path);
///     return ReadAllAsync(stream);   // CC027: stream disposed before the task completes
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ReturnedTaskUsingDisposedAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC027";

    private static readonly LocalizableString Title = "Returned task uses a disposed 'using' resource";
    private static readonly LocalizableString MessageFormat = "'{0}' is disposed when this method returns, before the returned task completes; make the method async and await the call";
    private static readonly LocalizableString Description = "Returning a task whose receiver is a using-scoped resource disposes the resource before the task completes; await it instead.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
    }

    private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        Analyze(context, method.Modifiers, method.ReturnType, method.Body);
    }

    private void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var local = (LocalFunctionStatementSyntax)context.Node;
        Analyze(context, local.Modifiers, local.ReturnType, local.Body);
    }

    private static void Analyze(
        SyntaxNodeAnalysisContext context, SyntaxTokenList modifiers, TypeSyntax returnType, BlockSyntax? body)
    {
        // An async method cannot `return aTask;` directly (it would be CS0029), so the bug only
        // arises in a non-async Task-returning method with a block body.
        if (body == null || modifiers.Any(SyntaxKind.AsyncKeyword))
            return;
        if (!CancellationTokenHelpers.IsAsyncReturnType(
                context.SemanticModel.GetTypeInfo(returnType, context.CancellationToken).Type))
            return;

        // Collect the locals declared by a `using` (declaration or statement) that belong to this
        // body (not a nested function). Both forms dispose the resource before the returned task
        // completes.
        var usingLocals = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var node in DescendantsInOwnScope(body))
        {
            VariableDeclarationSyntax? usingDeclaration = node switch
            {
                LocalDeclarationStatementSyntax local when !local.UsingKeyword.IsKind(SyntaxKind.None) => local.Declaration,
                UsingStatementSyntax usingStatement => usingStatement.Declaration,
                _ => null,
            };
            if (usingDeclaration == null)
                continue;

            foreach (var variable in usingDeclaration.Variables)
            {
                if (context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is ILocalSymbol local)
                    usingLocals.Add(local);
            }
        }

        if (usingLocals.Count == 0)
            return;

        foreach (var returnStatement in DescendantsInOwnScope(body).OfType<ReturnStatementSyntax>())
        {
            if (returnStatement.Expression == null)
                continue;

            var receiver = GetLeftmostReceiver(returnStatement.Expression);
            var receiverSymbol = context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol;
            if (receiverSymbol != null && usingLocals.Contains(receiverSymbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule, returnStatement.Expression.GetLocation(), receiverSymbol.Name));
            }
        }
    }

    /// <summary>
    /// Descendants of <paramref name="body"/> that belong to it, not to a nested local function or
    /// lambda (whose own using-declarations and returns are a separate scope).
    /// </summary>
    private static IEnumerable<SyntaxNode> DescendantsInOwnScope(SyntaxNode body) =>
        body.DescendantNodes(descendIntoChildren: child =>
            child is not LocalFunctionStatementSyntax && child is not AnonymousFunctionExpressionSyntax);

    /// <summary>
    /// Walks an invocation/member-access (and conditional-access/parenthesis) chain down to its
    /// left-most receiver expression.
    /// </summary>
    private static ExpressionSyntax GetLeftmostReceiver(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case InvocationExpressionSyntax invocation:
                    expression = invocation.Expression;
                    break;
                case MemberAccessExpressionSyntax memberAccess:
                    expression = memberAccess.Expression;
                    break;
                case ConditionalAccessExpressionSyntax conditional:
                    expression = conditional.Expression;
                    break;
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                default:
                    return expression;
            }
        }
    }
}
