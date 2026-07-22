using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
    /// The shared tail of the token-propagation rules (CC002/CC003/CC004): given an invocation
    /// the caller has already gated (rule-specific name/namespace/type checks), reports
    /// <paramref name="rule"/> when an in-scope token exists, the call does not already pass
    /// one, the call sits in executable code, and a token-accepting overload is available.
    /// </summary>
    /// <remarks>
    /// The diagnostic lands on the invoked member name, carries <c>TokenParameterName</c>
    /// (the in-scope token to pass) and <c>TokenArgumentName</c> (the target overload's
    /// parameter name, for named-argument fixes), and formats the rule message with the method
    /// name and token name.
    /// </remarks>
    public static void ReportIfTokenNotPropagated(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        DiagnosticDescriptor rule)
    {
        // Check if a CancellationToken was already passed in the invocation
        if (HasCancellationTokenArgument(invocation, context.SemanticModel))
            return;

        // Find the nearest in-scope CancellationToken parameter — from a containing local
        // function, lambda, constructor, method, or primary constructor.
        var tokenParameter = FindEnclosingCancellationTokenParameter(invocation, context.SemanticModel);
        if (tokenParameter == null)
            return;

        // An invocation inside an expression tree is data, not executable code: the token cannot
        // be propagated into it and the fix would not compile.
        if (IsWithinExpressionTree(invocation, context.SemanticModel))
            return;

        // Only fire when there is an overload whose non-token parameters match this call's by type, so
        // appending the in-scope token produces a call that actually compiles. A merely-same-name token
        // overload with different parameters (e.g. StreamWriter.WriteAsync(string), whose token overload
        // takes ReadOnlyMemory<char>) would otherwise yield a non-compiling fix.
        var overloadTokenName = GetTypeCompatibleTokenParameterName(methodSymbol);
        if (overloadTokenName == null)
            return;

        var properties = ImmutableDictionary.CreateBuilder<string, string?>();
        properties.Add("TokenParameterName", tokenParameter.Name);
        properties.Add("TokenArgumentName", overloadTokenName);

        var diagnostic = Diagnostic.Create(
            rule,
            invocation.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Name.GetLocation()
                : invocation.Expression.GetLocation(),
            properties.ToImmutable(),
            methodSymbol.Name,
            tokenParameter.Name);

        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Returns true when the nearest enclosing function (method, local function, lambda, anonymous
    /// method, or synthesized top-level entry point) is async. The search stops at the first
    /// function boundary, so a synchronous lambda inside an async method is correctly treated as
    /// non-async, and a property/accessor boundary ends the search.
    /// </summary>
    public static bool IsInAsyncFunction(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax method:
                    return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case LocalFunctionStatementSyntax local:
                    return local.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case AnonymousFunctionExpressionSyntax anonymous:
                    return anonymous.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case GlobalStatementSyntax global:
                    return HasTopLevelAwait((CompilationUnitSyntax)global.Parent!);
                case AccessorDeclarationSyntax:
                case BasePropertyDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    private static bool HasTopLevelAwait(CompilationUnitSyntax compilationUnit)
    {
        foreach (var global in compilationUnit.Members.OfType<GlobalStatementSyntax>())
        {
            foreach (var token in global.DescendantTokens())
            {
                if (!token.IsKind(SyntaxKind.AwaitKeyword))
                    continue;

                // Await inside a nested lambda/local function belongs to that function, not the
                // synthesized top-level entry point.
                var belongsToNestedFunction = token.Parent?.AncestorsAndSelf()
                    .TakeWhile(node => node != global)
                    .Any(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax) == true;

                if (!belongsToNestedFunction)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="parameter"/> is referenced at runtime within
    /// <paramref name="body"/> — including inside nested lambdas and local functions, where the
    /// parameter is captured. Compile-time-only <c>nameof</c> references, simple assignments, and
    /// <c>out</c> arguments that only overwrite the parameter are ignored. Used to tell an observed
    /// token from a dead one.
    /// </summary>
    public static bool IsParameterReferenced(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.Text != parameter.Name)
                continue;

            if (!SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol, parameter))
            {
                continue;
            }

            if (IsInsideNameof(identifier, semanticModel, cancellationToken))
                continue;

            var parameterReference = semanticModel.GetOperation(identifier, cancellationToken)
                as IParameterReferenceOperation;

            if (parameterReference?.Parent is ISimpleAssignmentOperation assignment
                && ReferenceEquals(assignment.Target, parameterReference))
            {
                continue;
            }

            if (parameterReference?.Parent is IArgumentOperation argument
                && argument.Parameter?.RefKind == RefKind.Out
                && ReferenceEquals(argument.Value, parameterReference))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="node"/> is contained by a compile-time-only
    /// <c>nameof</c> operation.
    /// </summary>
    public static bool IsInsideNameof(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        for (var operation = semanticModel.GetOperation(node, cancellationToken);
             operation != null;
             operation = operation.Parent)
        {
            if (operation is INameOfOperation)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="body"/> reads a member named <paramref name="memberName"/>
    /// off <paramref name="parameter"/> at runtime (e.g. <c>context.CancellationToken</c>).
    /// Compile-time-only <c>nameof</c> references are ignored. Used by the context-token rules
    /// (CC020/CC021) to tell an observed token from an ignored one.
    /// </summary>
    public static bool AccessesMember(
        SyntaxNode body,
        IParameterSymbol parameter,
        string memberName,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var memberAccess in body.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Name.Identifier.Text != memberName)
                continue;

            if (SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol, parameter) &&
                !IsInsideNameof(memberAccess, semanticModel, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="parameter"/> is passed as an argument somewhere in
    /// <paramref name="body"/>, including as the receiver of a reduced extension invocation — i.e.
    /// it is handed off to another method that may observe it — so a rule that requires the
    /// parameter to be used locally should stand down.
    /// </summary>
    public static bool ParameterEscapesAsArgument(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.Text != parameter.Name)
                continue;
            if (!SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol, parameter))
                continue;

            if (identifier.Parent is ArgumentSyntax)
                return true;

            if (identifier.Parent is MemberAccessExpressionSyntax { Expression: var receiver } memberAccess &&
                receiver == identifier &&
                memberAccess.Parent is InvocationExpressionSyntax invocation &&
                semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is
                    IMethodSymbol { ReducedFrom: not null })
            {
                return true;
            }
        }

        return false;
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
        return GetOverloadTokenParameterName(methodSymbol) != null;
    }

    /// <summary>
    /// Returns the declared name of the <c>CancellationToken</c> parameter on the overload the
    /// fixed call is most likely to bind to, or <c>null</c> when no overload accepts one. Fixers
    /// need the name to emit a named argument (<c>cancellationToken: ct</c>) when the call
    /// already uses named arguments — naming the wrong overload's parameter would be CS1739.
    /// </summary>
    /// <remarks>
    /// Preference order: an overload whose non-token parameters match the bound method's
    /// parameters by type, then one matching by count, then the first token-bearing overload.
    /// </remarks>
    public static string? GetOverloadTokenParameterName(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return null;

        // Compare against the unreduced original definition so extension methods include their
        // `this` parameter on both sides.
        var boundParameters = (methodSymbol.ReducedFrom ?? methodSymbol).OriginalDefinition.Parameters;

        string? countMatch = null;
        string? fallback = null;

        foreach (var overload in containingType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>())
        {
            var tokenParameter = overload.Parameters.FirstOrDefault(p => IsCancellationToken(p.Type));
            if (tokenParameter == null)
                continue;

            fallback ??= tokenParameter.Name;

            var nonTokenParameters = overload.Parameters
                .Where(p => !IsCancellationToken(p.Type))
                .ToImmutableArray();
            if (nonTokenParameters.Length != boundParameters.Length)
                continue;

            countMatch ??= tokenParameter.Name;

            var typesMatch = true;
            for (var i = 0; i < nonTokenParameters.Length; i++)
            {
                if (!ParameterTypesEquivalent(
                        nonTokenParameters[i].Type, boundParameters[i].Type))
                {
                    typesMatch = false;
                    break;
                }
            }

            if (typesMatch)
                return tokenParameter.Name;
        }

        return countMatch ?? fallback;
    }

    /// <summary>
    /// Returns the token parameter name of an overload whose non-token parameters match the bound
    /// call's parameters by type — the only overload to which the in-scope token can be appended so
    /// the resulting call still compiles — or <c>null</c> when none exists.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="GetOverloadTokenParameterName"/>, this never falls back to a count-only or
    /// first-token-bearing overload, so the propagation rules (CC002/CC003/CC004) do not fire when the
    /// only token-accepting overload has incompatible parameters. For example,
    /// <c>StreamWriter.WriteAsync(string)</c> has no <c>WriteAsync(string, CancellationToken)</c> — its
    /// token overloads take <c>ReadOnlyMemory&lt;char&gt;</c>/<c>StringBuilder</c> — so appending a token
    /// to a <c>WriteAsync(text)</c> call would not compile; this returns <c>null</c> and the rule stays
    /// quiet.
    /// </remarks>
    public static string? GetTypeCompatibleTokenParameterName(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return null;

        var method = (methodSymbol.ReducedFrom ?? methodSymbol).OriginalDefinition;

        // Case B: the bound overload itself declares a CancellationToken parameter the call omitted
        // (e.g. `query.ToListAsync()` binding to `ToListAsync(..., CancellationToken = default)`).
        // Supplying the token binds to this same overload, so the fix always compiles.
        var ownToken = method.Parameters.FirstOrDefault(p => IsCancellationToken(p.Type));
        if (ownToken != null)
            return ownToken.Name;

        // Case A: a sibling overload whose non-token parameters match this call's parameters by type
        // (e.g. `Task.Delay(100)` → `Task.Delay(int, CancellationToken)`).
        var boundParameters = method.Parameters;

        foreach (var overload in containingType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>())
        {
            var tokenParameter = overload.Parameters.FirstOrDefault(p => IsCancellationToken(p.Type));
            if (tokenParameter == null)
                continue;

            var nonTokenParameters = overload.Parameters
                .Where(p => !IsCancellationToken(p.Type))
                .ToImmutableArray();
            if (nonTokenParameters.Length != boundParameters.Length)
                continue;

            var typesMatch = true;
            for (var i = 0; i < nonTokenParameters.Length; i++)
            {
                if (!ParameterTypesEquivalent(
                        nonTokenParameters[i].Type, boundParameters[i].Type))
                {
                    typesMatch = false;
                    break;
                }
            }

            if (typesMatch)
                return tokenParameter.Name;
        }

        return null;
    }

    /// <summary>
    /// Compares two parameter types from sibling overloads' <c>OriginalDefinition</c>s for
    /// overload-matching. Identical to <see cref="SymbolEqualityComparer.Default"/> except that two
    /// generic type parameters are treated as equivalent when they share an ordinal and kind — each
    /// overload of a generic method owns a distinct type-parameter symbol, so a plain symbol comparison
    /// would wrongly reject <c>FooAsync&lt;T&gt;(T)</c> against <c>FooAsync&lt;T&gt;(T, CancellationToken)</c>.
    /// Array and constructed generic types are compared structurally so the rule applies through them.
    /// </summary>
    private static bool ParameterTypesEquivalent(ITypeSymbol a, ITypeSymbol b)
    {
        if (a is ITypeParameterSymbol pa && b is ITypeParameterSymbol pb)
            return pa.Ordinal == pb.Ordinal && pa.TypeParameterKind == pb.TypeParameterKind;

        if (a is IArrayTypeSymbol aa && b is IArrayTypeSymbol ab)
            return aa.Rank == ab.Rank && ParameterTypesEquivalent(aa.ElementType, ab.ElementType);

        if (a is INamedTypeSymbol na && b is INamedTypeSymbol nb && na.IsGenericType && nb.IsGenericType)
        {
            if (!SymbolEqualityComparer.Default.Equals(na.OriginalDefinition, nb.OriginalDefinition))
                return false;
            if (na.TypeArguments.Length != nb.TypeArguments.Length)
                return false;
            for (var i = 0; i < na.TypeArguments.Length; i++)
            {
                if (!ParameterTypesEquivalent(na.TypeArguments[i], nb.TypeArguments[i]))
                    return false;
            }
            return true;
        }

        return SymbolEqualityComparer.Default.Equals(a, b);
    }
}
