using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a method which receives an <c>HttpContext</c> but never observes its
/// <c>RequestAborted</c> cancellation token.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC021
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// ASP.NET Core exposes the request's cancellation token as
/// <c>HttpContext.RequestAborted</c>, signalled when the client disconnects. As with gRPC's
/// <c>ServerCallContext.CancellationToken</c> (CC020), it is a property rather than a parameter, so
/// the general propagation rule (CC002) cannot see it. Async middleware/handlers that ignore it keep
/// working on a response nobody will read. Reported as <b>Info</b> because an <c>HttpContext</c> is
/// frequently taken for reasons unrelated to cancellation.
/// </para>
/// <para>
/// <b>What it detects:</b> a method with a <c>Microsoft.AspNetCore.Http.HttpContext</c> parameter
/// whose body performs asynchronous work (contains an <c>await</c>) but never reads
/// <c>context.RequestAborted</c> and never passes the context on to another method.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task InvokeAsync(HttpContext context)   // CC021
/// {
///     await _next(context);   // wait — passing context on satisfies the rule; this would not flag
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RequestAbortedAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC021";

    private static readonly LocalizableString Title = "HttpContext.RequestAborted is not observed";
    private static readonly LocalizableString MessageFormat = "Method does async work but never observes '{0}.RequestAborted'";
    private static readonly LocalizableString Description = "A method holding an HttpContext should flow HttpContext.RequestAborted into its async calls so work stops when the client disconnects.";
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

        var contextParameter = symbol.Parameters.FirstOrDefault(IsHttpContext);
        if (contextParameter == null)
            return;

        if (!body.DescendantNodes().OfType<AwaitExpressionSyntax>().Any())
            return;

        if (CancellationTokenHelpers.AccessesMember(body, contextParameter, "RequestAborted", context.SemanticModel, context.CancellationToken) ||
            CancellationTokenHelpers.ParameterEscapesAsArgument(body, contextParameter, context.SemanticModel, context.CancellationToken))
            return;

        var location = method.ParameterList.Parameters
            .FirstOrDefault(p => p.Identifier.Text == contextParameter.Name)?.Identifier.GetLocation()
            ?? method.Identifier.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, contextParameter.Name));
    }

    private static bool IsHttpContext(IParameterSymbol parameter)
    {
        var type = parameter.Type;
        return type.Name == "HttpContext" &&
               type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Http";
    }
}
