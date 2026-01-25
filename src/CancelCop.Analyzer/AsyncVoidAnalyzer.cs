using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects async void methods which are a dangerous pattern.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC010
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Async void methods are "fire and forget" - exceptions thrown in them cannot be caught
/// by the caller. They crash the application if unhandled. The only valid use case is
/// for event handlers.
/// </para>
/// <para>
/// <b>What it detects:</b>
/// <list type="bullet">
/// <item>Methods declared as async void</item>
/// <item>Local functions declared as async void</item>
/// <item>Lambdas that are async void (unless used as event handlers)</item>
/// </list>
/// </para>
/// <para>
/// <b>What it ignores:</b>
/// <list type="bullet">
/// <item>Event handlers (methods with sender/EventArgs signature)</item>
/// <item>Methods overriding a base async void method</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// public async void ProcessAsync()  // CC010
/// {
///     await Task.Delay(1000);
/// }
///
/// // Fixed:
/// public async Task ProcessAsync()
/// {
///     await Task.Delay(1000);
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AsyncVoidAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC010";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Avoid async void methods";
    private static readonly LocalizableString MessageFormat = "Method '{0}' should return Task instead of void";
    private static readonly LocalizableString Description = "Async void methods cannot have their exceptions caught and should be avoided. Use async Task instead, except for event handlers.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
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
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.ParenthesizedLambdaExpression);
        context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.SimpleLambdaExpression);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;

        // Check if method is async
        if (!methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;

        // Check if return type is void
        if (methodDeclaration.ReturnType is not PredefinedTypeSyntax predefinedType ||
            !predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword))
            return;

        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
            return;

        // Skip event handlers (object sender, EventArgs e pattern)
        if (IsEventHandler(methodSymbol))
            return;

        // Skip methods that override a base method (can't change signature)
        if (methodSymbol.IsOverride)
            return;

        // Skip interface implementations
        if (IsInterfaceImplementation(methodSymbol))
            return;

        var diagnostic = Diagnostic.Create(
            Rule,
            methodDeclaration.Identifier.GetLocation(),
            methodDeclaration.Identifier.Text);

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var localFunction = (LocalFunctionStatementSyntax)context.Node;

        // Check if local function is async
        if (!localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;

        // Check if return type is void
        if (localFunction.ReturnType is not PredefinedTypeSyntax predefinedType ||
            !predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword))
            return;

        var diagnostic = Diagnostic.Create(
            Rule,
            localFunction.Identifier.GetLocation(),
            localFunction.Identifier.Text);

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeLambda(SyntaxNodeAnalysisContext context)
    {
        var lambda = (LambdaExpressionSyntax)context.Node;

        // Check if lambda is async
        if (!lambda.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;

        // Get the type info to check if it's void-returning
        var typeInfo = context.SemanticModel.GetTypeInfo(lambda);
        if (typeInfo.ConvertedType is not INamedTypeSymbol namedType)
            return;

        // Check if it's an Action delegate (void-returning)
        if (!IsVoidReturningDelegate(namedType))
            return;

        // Skip if being assigned to an event
        if (IsEventAssignment(lambda))
            return;

        var location = lambda.AsyncKeyword.GetLocation();
        var diagnostic = Diagnostic.Create(
            Rule,
            location,
            "lambda");

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsEventHandler(IMethodSymbol method)
    {
        var parameters = method.Parameters;

        // Standard event handler pattern: (object sender, EventArgs e)
        if (parameters.Length == 2)
        {
            var firstParam = parameters[0];
            var secondParam = parameters[1];

            // Check for sender parameter (object or derived)
            if (firstParam.Type.SpecialType == SpecialType.System_Object ||
                firstParam.Name.Equals("sender", System.StringComparison.OrdinalIgnoreCase))
            {
                // Check for EventArgs parameter (EventArgs or derived)
                if (IsEventArgsType(secondParam.Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsEventArgsType(ITypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            if (current.Name == "EventArgs" && current.ContainingNamespace?.ToString() == "System")
                return true;

            current = current.BaseType;
        }

        return false;
    }

    private static bool IsInterfaceImplementation(IMethodSymbol method)
    {
        foreach (var iface in method.ContainingType.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                var implementation = method.ContainingType.FindImplementationForInterfaceMember(member);
                if (SymbolEqualityComparer.Default.Equals(implementation, method))
                    return true;
            }
        }

        return false;
    }

    private static bool IsVoidReturningDelegate(INamedTypeSymbol type)
    {
        // Check for Action delegates
        if (type.Name.StartsWith("Action") &&
            type.ContainingNamespace?.ToString() == "System")
        {
            return true;
        }

        // Check for custom void-returning delegates
        if (type.TypeKind == TypeKind.Delegate)
        {
            var invokeMethod = type.DelegateInvokeMethod;
            if (invokeMethod?.ReturnsVoid == true)
                return true;
        }

        return false;
    }

    private static bool IsEventAssignment(LambdaExpressionSyntax lambda)
    {
        // Check if the lambda is being used in an event subscription
        var parent = lambda.Parent;

        // += operator for event subscription
        if (parent is AssignmentExpressionSyntax assignment &&
            assignment.IsKind(SyntaxKind.AddAssignmentExpression))
        {
            return true;
        }

        // Event handler property initialization
        if (parent is EqualsValueClauseSyntax equalsValue &&
            equalsValue.Parent is PropertyDeclarationSyntax)
        {
            return true;
        }

        return false;
    }
}
