using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a broad <c>catch</c> which swallows
/// <c>OperationCanceledException</c> — turning cancellation into a normal, handled path.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC019
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>OperationCanceledException</c> is how cooperative cancellation unwinds. A catch-all or
/// <c>catch (Exception)</c> over awaited work that does not rethrow treats a cancelled operation as
/// a generic failure (or silently succeeds), so callers awaiting the cancellation never see it and
/// shutdown logic misbehaves. The fix is a <c>when</c> filter that excludes cancellation, or
/// rethrowing it. Reported as <b>Info</b> because broad boundary handlers are sometimes intended.
/// </para>
/// <para>
/// <b>What it detects:</b> a <c>catch</c> with no exception type, or one catching
/// <c>System.Exception</c>, that has no <c>when</c> filter, whose <c>try</c> block contains an
/// <c>await</c> in the current function scope (including <c>await foreach</c> and
/// <c>await using</c>), and whose body does not propagate cancellation. Awaits owned by a nested
/// local or anonymous function do not execute as part of the <c>try</c> itself. Direct type-pattern
/// rethrows account for positive/negated polarity and overlap with the cancellation hierarchy.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// try { await DoAsync(token); }
/// catch (Exception ex)            // CC019 - also swallows OperationCanceledException
/// {
///     Log(ex);
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SwallowedCancellationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC019";

    private static readonly LocalizableString Title = "Broad catch swallows OperationCanceledException";
    private static readonly LocalizableString MessageFormat = "This catch swallows OperationCanceledException; add a 'when' filter that excludes cancellation or rethrow it";
    private static readonly LocalizableString Description = "A catch-all or catch (Exception) over awaited work that does not rethrow treats cancellation as a normal error.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeCatch, SyntaxKind.CatchClause);
    }

    private void AnalyzeCatch(SyntaxNodeAnalysisContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;

        // A 'when' filter may already exclude cancellation; stay quiet to avoid second-guessing it.
        if (catchClause.Filter != null)
            return;

        // Only broad catches accidentally swallow cancellation: a catch-all, or catch (Exception).
        if (!CatchesEverything(catchClause, context.SemanticModel, context.CancellationToken))
            return;

        if (catchClause.Parent is not TryStatementSyntax tryStatement)
            return;

        // Cancellation can only surface where awaited work runs.
        if (!ContainsAwaitInCurrentScope(tryStatement.Block))
            return;

        // A rethrow lets cancellation propagate, so the catch is not swallowing it.
        if (RethrowsOrThrows(
                catchClause.Block,
                catchClause,
                context.SemanticModel,
                context.CancellationToken))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, catchClause.CatchKeyword.GetLocation()));
    }

    private static bool ContainsAwaitInCurrentScope(SyntaxNode block)
    {
        return block.DescendantNodes(descendIntoChildren: child =>
                child is not LocalFunctionStatementSyntax &&
                child is not AnonymousFunctionExpressionSyntax)
            .Any(IsAwaitedOperation);
    }

    private static bool IsAwaitedOperation(SyntaxNode node)
    {
        return node switch
        {
            AwaitExpressionSyntax => true,
            CommonForEachStatementSyntax forEach =>
                forEach.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            LocalDeclarationStatementSyntax declaration =>
                declaration.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            UsingStatementSyntax usingStatement =>
                usingStatement.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            _ => false,
        };
    }

    private static bool CatchesEverything(
        CatchClauseSyntax catchClause, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        // No declaration => `catch { }`, which catches everything.
        if (catchClause.Declaration?.Type is not { } typeSyntax)
            return true;

        var type = semanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type;
        return type?.Name == "Exception" &&
               type.ContainingNamespace?.ToDisplayString() == "System";
    }

    private static bool RethrowsOrThrows(
        SyntaxNode? block,
        CatchClauseSyntax catchClause,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (block == null)
            return false;

        // Descend, but not into nested functions whose throws belong to a different scope.
        foreach (var node in block.DescendantNodes(descendIntoChildren: child =>
                     child is not LocalFunctionStatementSyntax &&
                     child is not AnonymousFunctionExpressionSyntax))
        {
            if ((node is ThrowStatementSyntax or ThrowExpressionSyntax) &&
                !IsRestrictedToUnrelatedException(node, catchClause, semanticModel, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRestrictedToUnrelatedException(
        SyntaxNode throwNode,
        CatchClauseSyntax catchClause,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (catchClause.Declaration is not { } declaration ||
            semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not { } caughtException ||
            semanticModel.Compilation.GetTypeByMetadataName("System.OperationCanceledException")
                is not { } cancellationException)
        {
            return false;
        }

        foreach (var ifStatement in throwNode.Ancestors().OfType<IfStatementSyntax>())
        {
            if (!ifStatement.Statement.Span.Contains(throwNode.Span) ||
                !TryGetTypeTest(
                    ifStatement.Condition,
                    out var identifier,
                    out var typeSyntax,
                    out var isNegated) ||
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    caughtException) ||
                semanticModel.GetSymbolInfo(typeSyntax, cancellationToken).Symbol is not INamedTypeSymbol testedType)
            {
                continue;
            }

            var restrictsCancellation = isNegated
                ? CanOverlapCancellationHierarchy(cancellationException, testedType)
                : !CanMatch(cancellationException, testedType);
            if (restrictsCancellation)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetTypeTest(
        ExpressionSyntax condition,
        out IdentifierNameSyntax identifier,
        out SyntaxNode typeSyntax,
        out bool isNegated)
    {
        if (condition is IsPatternExpressionSyntax
            {
                Expression: IdentifierNameSyntax patternIdentifier,
                Pattern: TypePatternSyntax typePattern,
            })
        {
            identifier = patternIdentifier;
            typeSyntax = typePattern.Type;
            isNegated = false;
            return true;
        }

        if (condition is IsPatternExpressionSyntax
            {
                Expression: IdentifierNameSyntax negatedPatternIdentifier,
                Pattern: UnaryPatternSyntax
                {
                    RawKind: (int)SyntaxKind.NotPattern,
                    Pattern: ConstantPatternSyntax negatedTypePattern,
                },
            })
        {
            identifier = negatedPatternIdentifier;
            typeSyntax = negatedTypePattern.Expression;
            isNegated = true;
            return true;
        }

        if (condition is BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.IsExpression,
                Left: IdentifierNameSyntax binaryIdentifier,
                Right: TypeSyntax binaryType,
            })
        {
            identifier = binaryIdentifier;
            typeSyntax = binaryType;
            isNegated = false;
            return true;
        }

        identifier = null!;
        typeSyntax = null!;
        isNegated = false;
        return false;
    }

    private static bool CanOverlapCancellationHierarchy(
        INamedTypeSymbol cancellationException,
        INamedTypeSymbol testedType)
    {
        return CanMatch(cancellationException, testedType) ||
               CanMatch(testedType, cancellationException) ||
               testedType.TypeKind == TypeKind.Interface && !cancellationException.IsSealed;
    }

    private static bool CanMatch(INamedTypeSymbol sourceType, INamedTypeSymbol testedType)
    {
        for (var current = sourceType; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, testedType))
            {
                return true;
            }
        }

        return sourceType.AllInterfaces.Any(interfaceType =>
            SymbolEqualityComparer.Default.Equals(interfaceType, testedType));
    }
}
