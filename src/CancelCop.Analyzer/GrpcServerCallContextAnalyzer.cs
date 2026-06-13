using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a gRPC service method which receives a <c>ServerCallContext</c> but never
/// observes its <c>CancellationToken</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC020
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// In a gRPC service the per-call cancellation token is exposed as
/// <c>ServerCallContext.CancellationToken</c>, signalled when the client cancels the call or
/// disconnects. Because it is a property rather than a parameter, the general propagation rule
/// (CC002) cannot see it, so a method that does async work without threading
/// <c>context.CancellationToken</c> through keeps running after the caller is gone.
/// </para>
/// <para>
/// <b>What it detects:</b> a method with a <c>Grpc.Core.ServerCallContext</c> parameter whose body
/// performs asynchronous work (contains an <c>await</c>) but never reads
/// <c>context.CancellationToken</c> and never passes the context on to another method.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public override async Task&lt;Reply&gt; Handle(Request request, ServerCallContext context)  // CC020
/// {
///     await _db.SaveChangesAsync();   // context.CancellationToken ignored
///     return new Reply();
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GrpcServerCallContextAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC020";

    private static readonly LocalizableString Title = "gRPC method ignores ServerCallContext.CancellationToken";
    private static readonly LocalizableString MessageFormat = "gRPC method does async work but never observes '{0}.CancellationToken'";
    private static readonly LocalizableString Description = "A gRPC service method should flow ServerCallContext.CancellationToken into its async calls so the call is cancelled when the client disconnects.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        SyntaxNode? body = method.Body ?? (SyntaxNode?)method.ExpressionBody;
        if (body == null)
            return;

        if (context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is not IMethodSymbol symbol)
            return;

        var contextParameter = symbol.Parameters.FirstOrDefault(IsServerCallContext);
        if (contextParameter == null)
            return;

        // Only async work observes cancellation.
        if (!body.DescendantNodes().OfType<AwaitExpressionSyntax>().Any())
            return;

        // Observing context.CancellationToken (or handing the context off so a callee can) satisfies
        // the rule.
        if (AccessesCancellationToken(body, contextParameter, context.SemanticModel, context.CancellationToken) ||
            ContextEscapes(body, contextParameter, context.SemanticModel, context.CancellationToken))
            return;

        var location = method.ParameterList.Parameters
            .FirstOrDefault(p => p.Identifier.Text == contextParameter.Name)?.Identifier.GetLocation()
            ?? method.Identifier.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, contextParameter.Name));
    }

    private static bool IsServerCallContext(IParameterSymbol parameter)
    {
        var type = parameter.Type;
        return type.Name == "ServerCallContext" &&
               type.ContainingNamespace?.ToDisplayString() == "Grpc.Core";
    }

    private static bool AccessesCancellationToken(
        SyntaxNode body, IParameterSymbol contextParameter, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        foreach (var memberAccess in body.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Name.Identifier.Text != "CancellationToken")
                continue;

            if (SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol, contextParameter))
                return true;
        }

        return false;
    }

    private static bool ContextEscapes(
        SyntaxNode body, IParameterSymbol contextParameter, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.Text != contextParameter.Name)
                continue;
            if (identifier.Parent is not ArgumentSyntax)
                continue;

            if (SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol, contextParameter))
                return true;
        }

        return false;
    }
}
