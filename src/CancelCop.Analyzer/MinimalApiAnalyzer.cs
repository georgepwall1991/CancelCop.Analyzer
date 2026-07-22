using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MinimalApiAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CC005C";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Minimal API endpoint should have CancellationToken parameter";
    private static readonly LocalizableString MessageFormat = "Minimal API '{0}' endpoint handler should have a CancellationToken parameter";
    private static readonly LocalizableString Description = "ASP.NET Core Minimal API endpoint handlers should accept a CancellationToken parameter.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

    // Minimal API mapping methods
    private static readonly ImmutableHashSet<string> MapMethods = ImmutableHashSet.Create(
        "MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch"
    );

    private const string EndpointRouteBuilderInterfaceName = "IEndpointRouteBuilder";
    private const string AspNetCoreRoutingNamespace = "Microsoft.AspNetCore.Routing";
    private const string EndpointRouteBuilderExtensionsMetadataName =
        "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions";

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

        // Check if this is a MapGet/MapPost/etc. call
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (!MapMethods.Contains(methodName))
            return;

        var arguments = invocation.ArgumentList.Arguments;

        // Confirm the call targets an ASP.NET Core endpoint route builder, not an unrelated method
        // that merely shares the name. Reduced calls carry the route builder as their receiver;
        // positional unreduced calls carry it as argument zero and use the exact framework extension
        // type as their syntactic receiver.
        var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
        int handlerIndex;
        if (receiverType != null && ImplementsEndpointRouteBuilder(receiverType))
        {
            handlerIndex = 1;
        }
        else
        {
            var extensionType = context.SemanticModel.Compilation.GetTypeByMetadataName(
                EndpointRouteBuilderExtensionsMetadataName);
            if (extensionType == null ||
                arguments.Count < 3 ||
                arguments[0].NameColon != null ||
                arguments[1].NameColon != null ||
                arguments[2].NameColon != null ||
                !SymbolEqualityComparer.Default.Equals(
                    context.SemanticModel.GetSymbolInfo(memberAccess.Expression).Symbol,
                    extensionType))
            {
                return;
            }

            var routeBuilderType = context.SemanticModel.GetTypeInfo(arguments[0].Expression).Type;
            if (routeBuilderType == null || !ImplementsEndpointRouteBuilder(routeBuilderType))
                return;

            handlerIndex = 2;
        }

        if (arguments.Count <= handlerIndex)
            return;

        var handlerArgument = arguments[handlerIndex].Expression;

        // A parenthesized handler is the same handler: `(Handler)` must not evade analysis.
        while (handlerArgument is ParenthesizedExpressionSyntax parenthesized)
            handlerArgument = parenthesized.Expression;

        // Method-group handlers (`app.MapGet("/", Handler)`, `Handlers.Get`, `Handler<T>`)
        // reference a declared method whose signature we can inspect directly.
        if (handlerArgument is SimpleNameSyntax or MemberAccessExpressionSyntax)
        {
            AnalyzeMethodGroupHandler(context, handlerArgument, methodName);
            return;
        }

        // Otherwise only lambda handlers are analysed; delegate variables and other expressions
        // are not bound to an inspectable signature here.
        if (handlerArgument is not ParenthesizedLambdaExpressionSyntax and not SimpleLambdaExpressionSyntax)
            return;

        // Check if the lambda is async
        var isAsync = false;
        ParameterListSyntax? parameterList = null;

        if (handlerArgument is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
        {
            isAsync = parenthesizedLambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
            parameterList = parenthesizedLambda.ParameterList;
        }
        else if (handlerArgument is SimpleLambdaExpressionSyntax simpleLambda)
        {
            isAsync = simpleLambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
            // For simple lambda, we need to check if it has CancellationToken parameter
            var parameterSymbol = context.SemanticModel.GetDeclaredSymbol(simpleLambda.Parameter);
            if (parameterSymbol != null && CancellationTokenHelpers.IsCancellationToken(parameterSymbol.Type))
                return; // Already has CancellationToken
        }

        if (!isAsync)
            return;

        // Check if any parameter is CancellationToken
        if (parameterList != null)
        {
            foreach (var parameter in parameterList.Parameters)
            {
                var parameterSymbol = context.SemanticModel.GetDeclaredSymbol(parameter);
                if (parameterSymbol != null && CancellationTokenHelpers.IsCancellationToken(parameterSymbol.Type))
                    return; // Already has CancellationToken
            }
        }

        // Report diagnostic on the lambda expression
        var diagnostic = Diagnostic.Create(
            Rule,
            handlerArgument.GetLocation(),
            methodName);

        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Analyses a method-group handler argument: resolves the referenced method and reports when
    /// it is async-shaped (async modifier or Task/ValueTask-returning) without a
    /// <c>CancellationToken</c> parameter.
    /// </summary>
    private static void AnalyzeMethodGroupHandler(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax handlerArgument,
        string methodName)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(handlerArgument);

        // A method group converted to System.Delegate may bind directly (C# 10 natural type) or
        // surface as a single candidate. A delegate-typed variable resolves to a local/field
        // symbol instead and is filtered out here. Multiple candidates mean an ambiguous group —
        // stay quiet rather than guess.
        var handlerMethod = symbolInfo.Symbol as IMethodSymbol;
        if (handlerMethod == null && symbolInfo.CandidateSymbols.Length == 1)
            handlerMethod = symbolInfo.CandidateSymbols[0] as IMethodSymbol;
        if (handlerMethod == null)
            return;

        // `handler.Invoke` resolves to the delegate type's Invoke method — its signature belongs
        // to the delegate type, not to anything the developer can change here.
        if (handlerMethod.ContainingType?.TypeKind == TypeKind.Delegate)
            return;

        // A handler defined in another assembly has no editable signature in this solution.
        if (handlerMethod.DeclaringSyntaxReferences.Length == 0)
            return;

        // Mirror the lambda path's async-only gating: a synchronous handler returning a plain
        // value has nothing to cancel.
        if (!handlerMethod.IsAsync && !CancellationTokenHelpers.IsAsyncReturnType(handlerMethod.ReturnType))
            return;

        if (CancellationTokenHelpers.HasCancellationTokenParameter(handlerMethod))
            return;

        // The developer cannot add a parameter to an override/interface/extern signature here.
        if (CancellationTokenHelpers.IsSignatureExternallyControlled(handlerMethod))
            return;

        var diagnostic = Diagnostic.Create(
            Rule,
            handlerArgument.GetLocation(),
            methodName);

        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> is, or implements,
    /// <c>Microsoft.AspNetCore.Routing.IEndpointRouteBuilder</c> — the receiver contract of the
    /// minimal-API <c>MapGet</c>/<c>MapPost</c>/… extension methods.
    /// </summary>
    private static bool ImplementsEndpointRouteBuilder(ITypeSymbol type) =>
        // The self-check is required: AllInterfaces does not include the type itself, and the
        // canonical endpoint-module idiom — `static void Map(this IEndpointRouteBuilder routes)` —
        // calls MapGet on a receiver whose declared type *is* the interface.
        IsEndpointRouteBuilder(type) || type.AllInterfaces.Any(IsEndpointRouteBuilder);

    private static bool IsEndpointRouteBuilder(ITypeSymbol type) =>
        type.Name == EndpointRouteBuilderInterfaceName &&
        type.ContainingNamespace?.ToDisplayString() == AspNetCoreRoutingNamespace;
}
