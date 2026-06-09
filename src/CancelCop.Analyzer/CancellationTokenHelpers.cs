using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CancelCop.Analyzer;

/// <summary>
/// Shared helper methods for CancellationToken analysis.
/// </summary>
internal static class CancellationTokenHelpers
{
    private const string CancellationTokenTypeName = "CancellationToken";
    private const string SystemThreadingNamespace = "System.Threading";
    private const string SystemThreadingTasksNamespace = "System.Threading.Tasks";

    /// <summary>
    /// Checks if the given type is System.Threading.CancellationToken.
    /// </summary>
    public static bool IsCancellationToken(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        return type.Name == CancellationTokenTypeName &&
               type.ContainingNamespace?.ToString() == SystemThreadingNamespace;
    }

    /// <summary>
    /// Checks if the given type is an async return type (Task, Task&lt;T&gt;, ValueTask, or ValueTask&lt;T&gt;).
    /// </summary>
    public static bool IsAsyncReturnType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        var typeName = type.Name;
        var ns = type.ContainingNamespace?.ToString();

        // Check for Task or Task<T>
        if (typeName == "Task" && ns == SystemThreadingTasksNamespace)
            return true;

        // Check for ValueTask or ValueTask<T>
        if (typeName == "ValueTask" && ns == SystemThreadingTasksNamespace)
            return true;

        return false;
    }

    /// <summary>
    /// Checks if any argument in the invocation is a CancellationToken.
    /// </summary>
    public static bool HasCancellationTokenArgument(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        return invocation.ArgumentList.Arguments.Any(arg =>
        {
            var argType = semanticModel.GetTypeInfo(arg.Expression).Type;
            return IsCancellationToken(argType);
        });
    }

    /// <summary>
    /// Finds the first CancellationToken parameter in a method.
    /// </summary>
    public static IParameterSymbol? FindCancellationTokenParameter(IMethodSymbol? method)
    {
        return method?.Parameters.FirstOrDefault(p => IsCancellationToken(p.Type));
    }

    /// <summary>
    /// Checks if a method has a CancellationToken parameter.
    /// </summary>
    public static bool HasCancellationTokenParameter(IMethodSymbol? method)
    {
        return FindCancellationTokenParameter(method) != null;
    }

    /// <summary>
    /// Walks up from <paramref name="node"/> through the scopes that can declare a
    /// <c>CancellationToken</c> parameter — local functions, lambdas, and the containing method —
    /// and returns the nearest in-scope token parameter, or <c>null</c> if none is available.
    /// </summary>
    /// <remarks>
    /// A local function or anonymous function that has no token of its own does not stop the
    /// search: an outer scope's token is captured and remains usable from the inner body — unless
    /// the inner function is <c>static</c>, which forbids capturing enclosing parameters
    /// (CS8421/CS8820), so a tokenless static function ends the search with no token. A method or
    /// constructor parameter list is the outermost <em>local</em> scope, but a tokenless
    /// non-static member keeps searching one level further: a C# 12 primary-constructor parameter
    /// is captured and usable from instance-member bodies and initializers. Static members cannot
    /// capture primary-constructor parameters, and a non-primary constructor's body cannot
    /// reference them (CS9105), so those end the search. The first containing type declaration
    /// always ends the search — an outer type's parameters are never in scope.
    /// </remarks>
    public static IParameterSymbol? FindEnclosingCancellationTokenParameter(
        SyntaxNode node,
        SemanticModel semanticModel)
    {
        var current = node.Parent;
        while (current != null)
        {
            // Local function first (innermost), then lambda / anonymous method, then the
            // containing member, then the containing type's primary constructor.
            if (current is LocalFunctionStatementSyntax localFunction)
            {
                var token = FindCancellationTokenParameter(
                    semanticModel.GetDeclaredSymbol(localFunction) as IMethodSymbol);
                if (token != null)
                    return token;
                if (localFunction.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return null;
            }
            else if (current is AnonymousFunctionExpressionSyntax anonymousFunction)
            {
                var token = FindCancellationTokenParameter(
                    semanticModel.GetSymbolInfo(anonymousFunction).Symbol as IMethodSymbol);
                if (token != null)
                    return token;
                if (anonymousFunction.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return null;
            }
            else if (current is ConstructorDeclarationSyntax constructor)
            {
                // A non-primary constructor's body cannot reference primary-constructor
                // parameters (CS9105), so its own parameter list ends the search.
                return FindCancellationTokenParameter(
                    semanticModel.GetDeclaredSymbol(constructor) as IMethodSymbol);
            }
            else if (current is MethodDeclarationSyntax method)
            {
                var token = FindCancellationTokenParameter(
                    semanticModel.GetDeclaredSymbol(method) as IMethodSymbol);
                if (token != null)
                    return token;
                // A static member cannot capture primary-constructor parameters.
                if (method.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return null;
            }
            else if (current is OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax)
            {
                // Classic operators are static and never declare a token. (C# 14 instance
                // compound-assignment operators could capture a primary token, but staying quiet
                // there is the conservative side.)
                return null;
            }
            else if (current is BaseFieldDeclarationSyntax field)
            {
                // Covers field and event-field declarations alike: a static initializer runs
                // without instance context, so the primary token is unavailable (CS9105).
                if (field.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return null;
            }
            else if (current is BasePropertyDeclarationSyntax property)
            {
                if (property.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return null;
            }
            else if (current is TypeDeclarationSyntax typeDeclaration)
            {
                // Reaching the type means every enclosing member was a tokenless non-static
                // scope; the type's primary-constructor parameters (if any) are the last chance.
                return FindPrimaryConstructorTokenParameter(typeDeclaration, semanticModel);
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Finds a <c>CancellationToken</c> among the type's primary-constructor parameters, looking
    /// first at the syntactic parameter list and then — for partial types whose primary
    /// constructor is declared on another part — through the type symbol's constructors.
    /// </summary>
    private static IParameterSymbol? FindPrimaryConstructorTokenParameter(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel semanticModel)
    {
        if (typeDeclaration.ParameterList != null)
        {
            foreach (var parameter in typeDeclaration.ParameterList.Parameters)
            {
                if (semanticModel.GetDeclaredSymbol(parameter) is IParameterSymbol parameterSymbol &&
                    IsCancellationToken(parameterSymbol.Type))
                {
                    return parameterSymbol;
                }
            }

            return null;
        }

        // A partial part without the parameter list: the primary constructor is the instance
        // constructor whose declaring syntax is a type declaration (capture from any part is
        // legal, so the token must still be found).
        if (semanticModel.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol typeSymbol)
        {
            foreach (var constructor in typeSymbol.InstanceConstructors)
            {
                foreach (var reference in constructor.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax() is TypeDeclarationSyntax)
                        return FindCancellationTokenParameter(constructor);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true when the method's signature is dictated by another declaration the developer
    /// cannot freely change here — an <c>override</c>, an explicit or implicit interface
    /// implementation, or an <c>extern</c> method. Adding or reordering a parameter on such a
    /// method breaks compilation (CS0115/CS0535), so signature-shape rules must not fire on it.
    /// </summary>
    public static bool IsSignatureExternallyControlled(IMethodSymbol method)
    {
        if (method.IsOverride || method.IsExtern)
            return true;

        if (method.ExplicitInterfaceImplementations.Length > 0)
            return true;

        return ImplementsInterfaceMember(method);
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        if (containingType == null)
            return false;

        foreach (var @interface in containingType.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers())
            {
                if (member is not IMethodSymbol)
                    continue;

                var implementation = containingType.FindImplementationForInterfaceMember(member);
                if (implementation != null &&
                    SymbolEqualityComparer.Default.Equals(implementation, method))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="node"/> is lexically inside a lambda that is converted to a
    /// <c>System.Linq.Expressions.Expression&lt;T&gt;</c> expression tree. Code inside an expression
    /// tree is data — e.g. translated to SQL by an <c>IQueryable</c> provider — not executed, so a
    /// <c>CancellationToken</c> cannot be propagated into it (and an expression tree may not contain a
    /// call that uses optional/extra arguments anyway, CS0853/CS0854). Such calls must not be flagged.
    /// </summary>
    public static bool IsWithinExpressionTree(SyntaxNode node, SemanticModel semanticModel)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is LambdaExpressionSyntax lambda &&
                IsExpressionTreeType(semanticModel.GetTypeInfo(lambda).ConvertedType))
                return true;

            // A method or local-function body is executable code and can never sit inside an
            // expression tree, so reaching one means we have left any enclosing tree.
            if (current is MethodDeclarationSyntax || current is LocalFunctionStatementSyntax)
                break;
        }

        return false;
    }

    private static bool IsExpressionTreeType(ITypeSymbol? type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.Name == "Expression" &&
                current.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if any overload of the method accepts a CancellationToken parameter.
    /// </summary>
    public static bool HasOverloadWithCancellationToken(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return false;

        return containingType.GetMembers(methodSymbol.Name)
            .OfType<IMethodSymbol>()
            .Any(m => m.Parameters.Any(p => IsCancellationToken(p.Type)));
    }
}
