using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a MediatR <c>IRequestHandler.Handle</c> implementation without a
/// <c>CancellationToken</c> parameter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC005A
/// </para>
/// <para>
/// <b>Why this matters:</b> MediatR passes a <c>CancellationToken</c> to <c>Handle</c>; a handler
/// that drops it cannot be cancelled when the request is abandoned. (Because the real MediatR
/// interface mandates the token, this rule mostly assists a handler that does not yet satisfy the
/// interface, hence its lower product importance.)
/// </para>
/// <para>
/// <b>What it detects:</b> a <c>Handle</c> method on an <c>MediatR.IRequestHandler</c> implementer
/// that is async-shaped and has no <c>CancellationToken</c> parameter. The "Add CancellationToken
/// parameter" code fix applies.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task&lt;Unit&gt; Handle(MyRequest request)   // CC005A: add a CancellationToken
///     =&gt; await _db.SaveChangesAsync();
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MediatRHandlerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CC005A";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "MediatR handler method should have CancellationToken parameter";
    private static readonly LocalizableString MessageFormat = "MediatR handler method '{0}' should have a CancellationToken parameter";
    private static readonly LocalizableString Description = "MediatR IRequestHandler.Handle methods should accept a CancellationToken parameter.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
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
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
            return;

        // Check if method is async
        if (!methodSymbol.IsAsync && !CancellationTokenHelpers.IsAsyncReturnType(methodSymbol.ReturnType))
            return;

        // Check if this is a MediatR IRequestHandler implementation
        if (!IsRequestHandlerImplementation(methodSymbol))
            return;

        // Check if method already has CancellationToken parameter
        if (CancellationTokenHelpers.HasCancellationTokenParameter(methodSymbol))
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            methodDeclaration.Identifier.GetLocation(),
            methodSymbol.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsRequestHandlerImplementation(IMethodSymbol methodSymbol)
    {
        // Check if the containing type implements IRequestHandler<TRequest> or IRequestHandler<TRequest, TResponse>
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return false;

        var implementsRequestHandler = containingType.AllInterfaces.Any(i =>
        {
            var interfaceName = i.Name;
            var namespaceName = i.ContainingNamespace?.ToDisplayString();

            return (interfaceName == "IRequestHandler" && namespaceName == "MediatR") &&
                   methodSymbol.Name == "Handle";
        });

        return implementsRequestHandler;
    }
}
