using System.Linq;
using Microsoft.CodeAnalysis;
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
