using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects an externally reachable <c>IAsyncEnumerable&lt;T&gt;</c> iterator declared
/// without any <c>CancellationToken</c> parameter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC034
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// An async stream is long-lived by nature — the consumer pulls items one at a time, and the
/// producer stays suspended in between. Without a token there is no way to stop it: a consumer that
/// abandons the enumeration leaves the producer's pending work with nothing to cancel, and
/// <c>.WithCancellation(token)</c> at the call site has nothing to flow into.
/// </para>
/// <para>
/// <b>The gap this closes:</b> CC001 only covers methods returning <c>Task</c>/<c>ValueTask</c>, so
/// it never sees an iterator. CC011 requires <c>[EnumeratorCancellation]</c>, but only once a token
/// parameter exists. CC010 flags the consumer for not calling <c>.WithCancellation</c>. A stream
/// declared with no token at all therefore slips past all three: CC034 is the producer-side entry
/// point that makes the others reachable.
/// </para>
/// <para>
/// <b>Conservative by design:</b> only <c>public</c>/<c>protected</c> iterators are flagged, matching
/// CC001 — a private stream's callers are all in view. Signatures fixed by something else are
/// excluded, because adding a parameter would break the contract rather than fix it: interface
/// implementations, <c>override</c>s, and <c>extern</c> declarations. A method that merely
/// <i>returns</i> an <c>IAsyncEnumerable&lt;T&gt;</c> without yielding is a pass-through, not a
/// producer, and is left alone.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // CC034: nothing can stop this enumeration
/// public async IAsyncEnumerable&lt;int&gt; ReadAsync()
/// {
///     yield return 1;
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AsyncStreamMissingTokenAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC034";

    private static readonly LocalizableString Title =
        "Async iterator should have a CancellationToken parameter";
    private static readonly LocalizableString MessageFormat =
        "Async iterator '{0}' should have a CancellationToken parameter so the enumeration can be stopped";
    private static readonly LocalizableString Description =
        "An IAsyncEnumerable iterator without a CancellationToken cannot be cancelled, and a consumer's .WithCancellation(token) has nothing to flow into.";
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

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;

        // Only an actual iterator: a method that merely returns an IAsyncEnumerable is passing one
        // through, and its own signature is not what stops the enumeration.
        if (!HasYield(declaration))
            return;

        if (
            context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken)
            is not IMethodSymbol method
        )
            return;

        if (!IsAsyncEnumerable(method.ReturnType))
            return;

        if (CancellationTokenHelpers.HasCancellationTokenParameter(method))
            return;

        // Same reach test as CC001: a private stream's callers are all in view, so the omission is
        // a local decision rather than an API defect.
        if (
            method.DeclaredAccessibility
            is not (
                Accessibility.Public
                or Accessibility.Protected
                or Accessibility.ProtectedOrInternal
            )
        )
            return;

        // Adding a parameter to a signature someone else fixed would break the contract, not fix it.
        if (
            method.IsOverride
            || method.IsExtern
            || method.ExplicitInterfaceImplementations.Length > 0
            || ImplementsInterfaceMember(method)
        )
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), method.Name)
        );
    }

    /// <summary>
    /// Returns <c>true</c> when the declaration yields, stopping at nested functions — a
    /// <c>yield</c> inside a local function or lambda belongs to that function's own iterator, not
    /// to this one.
    /// </summary>
    private static bool HasYield(MethodDeclarationSyntax declaration) =>
        declaration
            .DescendantNodes(descendIntoChildren: node =>
                node == declaration
                || node is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
            )
            .Any(node => node is YieldStatementSyntax);

    private static bool IsAsyncEnumerable(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "IAsyncEnumerable", TypeArguments.Length: 1 } named
        && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";

    /// <summary>
    /// Returns <c>true</c> when the method implicitly implements an interface member, whose
    /// signature it does not control.
    /// </summary>
    private static bool ImplementsInterfaceMember(IMethodSymbol method) =>
        method.ContainingType?.AllInterfaces.Any(candidate =>
            candidate
                .GetMembers(method.Name)
                .OfType<IMethodSymbol>()
                .Any(member =>
                    SymbolEqualityComparer.Default.Equals(
                        method.ContainingType.FindImplementationForInterfaceMember(member),
                        method
                    )
                )
        ) == true;
}
