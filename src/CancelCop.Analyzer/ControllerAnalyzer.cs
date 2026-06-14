using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects an ASP.NET Core controller action method without a
/// <c>CancellationToken</c> parameter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC005B
/// </para>
/// <para>
/// <b>Why this matters:</b> ASP.NET Core binds a controller action's <c>CancellationToken</c>
/// parameter to <c>HttpContext.RequestAborted</c>, which fires when the client disconnects. An
/// action without one keeps doing work (DB queries, HTTP calls) for a response nobody will read.
/// </para>
/// <para>
/// <b>What it detects:</b> a public, non-static, async (or <c>Task</c>/<c>ValueTask</c>-returning)
/// method on a <c>Microsoft.AspNetCore.Mvc.ControllerBase</c>/<c>Controller</c> subclass that carries
/// an MVC HTTP-method attribute (<c>[HttpGet]</c>, …, matched by namespace identity including
/// subclasses) and has no token. Inherited <c>[NonAction]</c> methods are excluded. The
/// "Add CancellationToken parameter" code fix applies.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpGet]
/// public async Task&lt;IActionResult&gt; Get()           // CC005B: add a CancellationToken
///     =&gt; Ok(await _db.Users.ToListAsync());
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ControllerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CC005B";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Controller action method should have CancellationToken parameter";
    private static readonly LocalizableString MessageFormat = "Controller action method '{0}' should have a CancellationToken parameter";
    private static readonly LocalizableString Description = "ASP.NET Core controller action methods should accept a CancellationToken parameter.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri);

    // HTTP method attributes
    private static readonly ImmutableHashSet<string> HttpMethodAttributes = ImmutableHashSet.Create(
        "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch", "HttpHead", "HttpOptions"
    );

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

        // Check if method is async or returns Task/ValueTask
        if (!methodSymbol.IsAsync && !CancellationTokenHelpers.IsAsyncReturnType(methodSymbol.ReturnType))
            return;

        // Check if this is a controller action method
        if (!IsControllerActionMethod(methodSymbol))
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

    private static bool IsControllerActionMethod(IMethodSymbol methodSymbol)
    {
        // Only public instance methods are routed as actions; private/protected/static
        // methods (and [NonAction] methods) are never invoked by the routing system, so they
        // do not need a CancellationToken.
        if (methodSymbol.DeclaredAccessibility != Accessibility.Public || methodSymbol.IsStatic)
            return false;

        // Check if the containing type inherits from ControllerBase or Controller
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return false;

        var inheritsFromController = InheritsFromControllerBase(containingType);
        if (!inheritsFromController)
            return false;

        if (HasAttribute(methodSymbol, "NonActionAttribute", "Microsoft.AspNetCore.Mvc"))
            return false;

        // Check if the method has a real MVC HTTP method attribute (resolved by namespace
        // identity, including subclasses of the framework attributes).
        return methodSymbol.GetAttributes().Any(attr => IsMvcHttpMethodAttribute(attr.AttributeClass));
    }

    private static bool IsMvcHttpMethodAttribute(INamedTypeSymbol? attributeClass)
    {
        for (var type = attributeClass; type != null; type = type.BaseType)
        {
            var name = type.Name;
            var shortName = name.EndsWith("Attribute")
                ? name.Substring(0, name.Length - "Attribute".Length)
                : name;

            if (HttpMethodAttributes.Contains(shortName) &&
                type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Mvc")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttribute(IMethodSymbol methodSymbol, string attributeTypeName, string containingNamespace)
    {
        var shortName = attributeTypeName.Replace("Attribute", "");

        // [NonAction] is inheritable, so an override that inherits it from a base virtual action
        // is still non-routed even though the attribute is not declared on the override itself.
        for (IMethodSymbol? current = methodSymbol; current != null; current = current.OverriddenMethod)
        {
            var found = current.GetAttributes().Any(attr =>
            {
                var attributeClass = attr.AttributeClass;
                if (attributeClass == null)
                    return false;

                var name = attributeClass.Name;
                if (name != attributeTypeName && name != shortName)
                    return false;

                // Match by namespace so a user-defined attribute of the same name is not treated
                // as the framework attribute.
                return attributeClass.ContainingNamespace?.ToDisplayString() == containingNamespace;
            });

            if (found)
                return true;
        }

        return false;
    }

    private static bool InheritsFromControllerBase(INamedTypeSymbol typeSymbol)
    {
        var baseType = typeSymbol.BaseType;
        while (baseType != null)
        {
            // Check both name and namespace to avoid false positives with custom ControllerBase classes
            if ((baseType.Name == "ControllerBase" || baseType.Name == "Controller") &&
                baseType.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Mvc")
                return true;

            baseType = baseType.BaseType;
        }

        return false;
    }
}
