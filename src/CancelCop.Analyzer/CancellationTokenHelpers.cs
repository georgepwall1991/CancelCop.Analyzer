using System.Collections.Generic;
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
// Public rather than internal so the code-fix assembly can share the syntax builders: an analyzer
// that speculatively binds the call it is about to suggest must emit *that* call, and duplicating the
// builder across the two assemblies is how analyzer and fixer drift apart.
public static class CancellationTokenHelpers
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

        return type.Name == CancellationTokenTypeName
            && type.ContainingNamespace?.ToString() == SystemThreadingNamespace;
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
    /// Checks if any argument in the invocation is converted to a CancellationToken parameter.
    /// </summary>
    public static bool HasCancellationTokenArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        return invocation.ArgumentList.Arguments.Any(arg =>
        {
            var argType = semanticModel.GetTypeInfo(arg.Expression).ConvertedType;
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
        SemanticModel semanticModel
    )
    {
        var current = node.Parent;
        while (current != null)
        {
            // Local function first (innermost), then lambda / anonymous method, then the
            // containing member, then the containing type's primary constructor.
            if (current is LocalFunctionStatementSyntax localFunction)
            {
                var token = FindCancellationTokenParameter(
                    semanticModel.GetDeclaredSymbol(localFunction) as IMethodSymbol
                );
                if (token != null)
                    return token;
                if (localFunction.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return null;
            }
            else if (current is AnonymousFunctionExpressionSyntax anonymousFunction)
            {
                var token = FindCancellationTokenParameter(
                    semanticModel.GetSymbolInfo(anonymousFunction).Symbol as IMethodSymbol
                );
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
                    semanticModel.GetDeclaredSymbol(constructor) as IMethodSymbol
                );
            }
            else if (current is MethodDeclarationSyntax method)
            {
                var token = FindCancellationTokenParameter(
                    semanticModel.GetDeclaredSymbol(method) as IMethodSymbol
                );
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
        SemanticModel semanticModel
    )
    {
        if (typeDeclaration.ParameterList != null)
        {
            foreach (var parameter in typeDeclaration.ParameterList.Parameters)
            {
                if (
                    semanticModel.GetDeclaredSymbol(parameter) is IParameterSymbol parameterSymbol
                    && IsCancellationToken(parameterSymbol.Type)
                )
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
        DiagnosticDescriptor rule
    )
    {
        // Check if a CancellationToken was already passed in the invocation
        if (HasCancellationTokenArgument(invocation, context.SemanticModel))
            return;

        // Find the nearest in-scope CancellationToken parameter — from a containing local
        // function, lambda, constructor, method, or primary constructor.
        var tokenParameter = FindEnclosingCancellationTokenParameter(
            invocation,
            context.SemanticModel
        );
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
            tokenParameter.Name
        );

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
                var belongsToNestedFunction =
                    token
                        .Parent?.AncestorsAndSelf()
                        .TakeWhile(node => node != global)
                        .Any(node =>
                            node
                                is AnonymousFunctionExpressionSyntax
                                    or LocalFunctionStatementSyntax
                        ) == true;

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
        System.Threading.CancellationToken cancellationToken
    )
    {
        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.Text != parameter.Name)
                continue;

            if (
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    parameter
                )
            )
            {
                continue;
            }

            if (IsInsideNameof(identifier, semanticModel, cancellationToken))
                continue;

            var parameterReference =
                semanticModel.GetOperation(identifier, cancellationToken)
                as IParameterReferenceOperation;

            if (
                parameterReference?.Parent is ISimpleAssignmentOperation assignment
                && ReferenceEquals(assignment.Target, parameterReference)
            )
            {
                continue;
            }

            if (
                parameterReference?.Parent is IArgumentOperation argument
                && argument.Parameter?.RefKind == RefKind.Out
                && ReferenceEquals(argument.Value, parameterReference)
            )
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
        System.Threading.CancellationToken cancellationToken
    )
    {
        for (
            var operation = semanticModel.GetOperation(node, cancellationToken);
            operation != null;
            operation = operation.Parent
        )
        {
            if (operation is INameOfOperation)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="body"/> reads a member named <paramref name="memberName"/>
    /// off <paramref name="parameter"/> at runtime (e.g. <c>context.CancellationToken</c>,
    /// <c>context?.CancellationToken</c>, or
    /// <c>context?.CancellationToken.ThrowIfCancellationRequested()</c>).
    /// Compile-time-only <c>nameof</c> references are ignored. Used by the context-token rules
    /// (CC020/CC021) to tell an observed token from an ignored one.
    /// </summary>
    public static bool AccessesMember(
        SyntaxNode body,
        IParameterSymbol parameter,
        string memberName,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        foreach (var memberAccess in body.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Name.Identifier.Text != memberName)
                continue;

            if (
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol,
                    parameter
                ) && !IsInsideNameof(memberAccess, semanticModel, cancellationToken)
            )
                return true;
        }

        foreach (
            var memberBinding in body.DescendantNodes().OfType<MemberBindingExpressionSyntax>()
        )
        {
            if (memberBinding.Name.Identifier.Text != memberName)
                continue;

            var conditionalAccess = memberBinding
                .Ancestors()
                .OfType<ConditionalAccessExpressionSyntax>()
                .FirstOrDefault();
            if (conditionalAccess == null)
                continue;

            if (
                SymbolEqualityComparer.Default.Equals(
                    semanticModel
                        .GetSymbolInfo(conditionalAccess.Expression, cancellationToken)
                        .Symbol,
                    parameter
                ) && !IsInsideNameof(memberBinding, semanticModel, cancellationToken)
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="parameter"/> is passed as an argument somewhere in
    /// <paramref name="body"/>, including as the receiver of a direct or null-conditional reduced
    /// extension invocation — i.e. it is handed off to another method that may observe it — so a
    /// rule that requires the parameter to be used locally should stand down.
    /// </summary>
    public static bool ParameterEscapesAsArgument(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.Text != parameter.Name)
                continue;
            if (
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    parameter
                )
            )
                continue;

            if (identifier.Parent is ArgumentSyntax)
                return true;

            if (
                identifier.Parent
                    is MemberAccessExpressionSyntax { Expression: var receiver } memberAccess
                && receiver == identifier
                && memberAccess.Parent is InvocationExpressionSyntax invocation
                && semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol
                    is IMethodSymbol { ReducedFrom: not null }
            )
            {
                return true;
            }

            if (
                identifier.Parent is ConditionalAccessExpressionSyntax conditionalAccess
                && conditionalAccess.Expression == identifier
                && conditionalAccess.WhenNotNull is InvocationExpressionSyntax conditionalInvocation
                && conditionalInvocation.Expression is MemberBindingExpressionSyntax
                && semanticModel.GetSymbolInfo(conditionalInvocation, cancellationToken).Symbol
                    is IMethodSymbol { ReducedFrom: not null }
            )
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
                if (
                    implementation != null
                    && SymbolEqualityComparer.Default.Equals(implementation, method)
                )
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
            if (
                current is LambdaExpressionSyntax lambda
                && IsExpressionTreeType(semanticModel.GetTypeInfo(lambda).ConvertedType)
            )
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
            if (
                current.Name == "Expression"
                && current.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions"
            )
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
        var boundParameters = (methodSymbol.ReducedFrom ?? methodSymbol)
            .OriginalDefinition
            .Parameters;

        string? countMatch = null;
        string? fallback = null;

        foreach (
            var overload in containingType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>()
        )
        {
            var tokenParameter = overload.Parameters.FirstOrDefault(p =>
                IsCancellationToken(p.Type)
            );
            if (tokenParameter == null)
                continue;

            fallback ??= tokenParameter.Name;

            var nonTokenParameters = overload
                .Parameters.Where(p => !IsCancellationToken(p.Type))
                .ToImmutableArray();
            if (nonTokenParameters.Length != boundParameters.Length)
                continue;

            countMatch ??= tokenParameter.Name;

            var typesMatch = true;
            for (var i = 0; i < nonTokenParameters.Length; i++)
            {
                if (!ParameterTypesEquivalent(nonTokenParameters[i].Type, boundParameters[i].Type))
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

        foreach (
            var overload in containingType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>()
        )
        {
            var tokenParameter = overload.Parameters.FirstOrDefault(p =>
                IsCancellationToken(p.Type)
            );
            if (tokenParameter == null)
                continue;

            var nonTokenParameters = overload
                .Parameters.Where(p => !IsCancellationToken(p.Type))
                .ToImmutableArray();
            if (nonTokenParameters.Length != boundParameters.Length)
                continue;

            var typesMatch = true;
            for (var i = 0; i < nonTokenParameters.Length; i++)
            {
                if (!ParameterTypesEquivalent(nonTokenParameters[i].Type, boundParameters[i].Type))
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

        if (
            a is INamedTypeSymbol na
            && b is INamedTypeSymbol nb
            && na.IsGenericType
            && nb.IsGenericType
        )
        {
            if (
                !SymbolEqualityComparer.Default.Equals(na.OriginalDefinition, nb.OriginalDefinition)
            )
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

    /// <summary>
    /// Returns <c>true</c> when the enclosing syntax forbids <c>await</c>, so inserting one would
    /// turn compiling source into a compiler error.
    /// </summary>
    /// <remarks>
    /// The blocking call is still worth reporting in these contexts — a synchronous write inside a
    /// <c>lock</c> is exactly the kind of code that stalls a request thread — but resolving it means
    /// restructuring the lock, which is the author's decision, not a mechanical rewrite. The walk
    /// stops at function boundaries because a lambda declared inside a <c>lock</c> body has its own
    /// context where <c>await</c> is legal again.
    /// </remarks>
    public static bool AwaitIsForbiddenHere(SyntaxNode node)
    {
        // An unsafe context is lexical and propagates into nested functions, and it can come from an
        // `unsafe` modifier on the method or type rather than an `unsafe { }` block — so this walk
        // runs to the top rather than stopping at a function boundary (CS4004).
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            var isUnsafe = current switch
            {
                UnsafeStatementSyntax => true,
                LocalFunctionStatementSyntax local => local.Modifiers.Any(SyntaxKind.UnsafeKeyword),
                MemberDeclarationSyntax member => member.Modifiers.Any(SyntaxKind.UnsafeKeyword),
                _ => false,
            };

            if (isUnsafe)
                return true;
        }

        for (var current = node.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                // CS1996 / CS7013: await is not permitted anywhere inside these.
                case LockStatementSyntax:
                case CatchFilterClauseSyntax:
                    return true;

                // CS1995: inside a query, await is permitted only in the first `from` clause's
                // source expression and in a `join` clause's source. Elsewhere in the query it is
                // an error, so the position within the query decides. An allowed position still has
                // to keep walking: an enclosing lock or outer query can forbid it anyway.
                case QueryExpressionSyntax query when !IsAwaitablePositionInQuery(query, node):
                    return true;

                // A nested function re-establishes an await-capable context.
                case AnonymousFunctionExpressionSyntax:
                case LocalFunctionStatementSyntax:
                case BaseMethodDeclarationSyntax:
                case AccessorDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="node"/> sits in one of the two query positions where
    /// <c>await</c> is legal: the source expression of the initial <c>from</c> clause, or the source
    /// of a <c>join</c> clause (CS1995).
    /// </summary>
    private static bool IsAwaitablePositionInQuery(QueryExpressionSyntax query, SyntaxNode node)
    {
        if (query.FromClause.Expression.Contains(node))
            return true;

        return query
            .Body.Clauses.OfType<JoinClauseSyntax>()
            .Any(join => join.InExpression.Contains(node));
    }

    /// <summary>
    /// Speculatively binds <paramref name="expression"/> at <paramref name="position"/> and returns
    /// <c>true</c> when it resolves to <paramref name="expected"/>.
    /// </summary>
    /// <remarks>
    /// A rule that rewrites a call to a differently-named counterpart is only safe if the rewritten
    /// call binds to the method the rule had in mind. Symbol lookup on the declaring type is not
    /// enough — the receiver's own type may hide the counterpart with an unusable member, and
    /// implicit conversions can hand the call to an overload that signature comparison rejects.
    /// Asking the compiler is the only answer that matches what the user's build will do.
    /// </remarks>
    public static bool SpeculativelyBindsTo(
        SemanticModel semanticModel,
        int position,
        ExpressionSyntax expression,
        IMethodSymbol expected
    )
    {
        var bound =
            semanticModel
                .GetSpeculativeSymbolInfo(
                    position,
                    expression,
                    SpeculativeBindingOption.BindAsExpression
                )
                .Symbol as IMethodSymbol;

        return bound != null
            && SymbolEqualityComparer.Default.Equals(
                bound.OriginalDefinition,
                expected.OriginalDefinition
            );
    }

    /// <summary>
    /// Rewrites <paramref name="invocation"/> to call <paramref name="newName"/> instead, appending
    /// <paramref name="tokenName"/> as an argument when one is supplied: the shape a rule rewrites a
    /// blocking call into. Returns <c>null</c> for a receiver form it cannot rewrite.
    /// </summary>
    /// <remarks>
    /// The name is replaced on the existing member access rather than the expression being rebuilt,
    /// and the original argument list is extended rather than replaced, so trivia survives — both
    /// <c>process /* started above */ .WaitForExit()</c> and
    /// <c>process.WaitForExit(/* why this waits */)</c> keep their comments. Sharing one builder also
    /// keeps an analyzer's speculative binding and its fixer's output in step: the call that was
    /// checked is the call that gets written.
    /// </remarks>
    public static InvocationExpressionSyntax? BuildRenamedInvocation(
        InvocationExpressionSyntax invocation,
        string newName,
        string? tokenName
    )
    {
        // `host?.Process.WaitForExit()` reaches here as an ordinary member access, but the whole
        // invocation is the WhenNotNull branch of a conditional access. Wrapping just that branch in
        // an await yields `host?await.Process...`, which is not valid syntax — the await has to go
        // outside the `?.`, which is a restructuring rather than a rewrite.
        for (var current = invocation.Parent; current != null; current = current.Parent)
        {
            if (current is ConditionalAccessExpressionSyntax)
                return null;
            // A nested function is its own expression context: a lambda that merely happens to be an
            // argument inside someone else's `?.` chain is rewritten normally.
            if (
                current
                is StatementSyntax
                    or MemberDeclarationSyntax
                    or AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
            )
                break;
        }

        var target = invocation.Expression switch
        {
            // Trivia is carried over from the old name: a comment attached to it
            // (`process.WaitForExit/* why */()`) belongs to the call, not to the name being replaced.
            MemberAccessExpressionSyntax memberAccess => (ExpressionSyntax)
                memberAccess.WithName(
                    SyntaxFactory.IdentifierName(newName).WithTriviaFrom(memberAccess.Name)
                ),
            // An implicit receiver: an inherited member called without `this.`.
            IdentifierNameSyntax => SyntaxFactory.IdentifierName(newName),
            _ => null,
        };
        if (target is null)
            return null;

        var argumentList = invocation.ArgumentList;
        if (tokenName != null)
        {
            argumentList = argumentList.AddArguments(
                SyntaxFactory.Argument(IdentifierNameFor(tokenName))
            );
        }

        return invocation.WithExpression(target).WithArgumentList(argumentList);
    }

    /// <summary>
    /// Builds an identifier for <paramref name="name"/>, re-escaping it when the name is a C#
    /// keyword.
    /// </summary>
    /// <remarks>
    /// A symbol's <c>Name</c> drops the escape: <c>CancellationToken @event</c> is stored as
    /// <c>event</c>. Emitting that bare reparses as a keyword, so the generated source would not
    /// compile even though the synthesized tree binds.
    /// </remarks>
    public static IdentifierNameSyntax IdentifierNameFor(string name)
    {
        // Contextual keywords are escaped as well as reserved ones. `await` is only reserved inside
        // an async body — which is exactly where these rewrites land — and deciding per-context is
        // both fiddly and easy to get wrong. `@` is legal on any identifier, so escaping a
        // contextual keyword that did not strictly need it still compiles and still binds.
        if (
            SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
            && SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.None
        )
            return SyntaxFactory.IdentifierName(name);

        // The token needs the escape in its *text* but the bare name as its value — the single-string
        // factory would store "@event" as the value too, which then fails to bind.
        return SyntaxFactory.IdentifierName(
            SyntaxFactory.Identifier(
                SyntaxTriviaList.Empty,
                SyntaxKind.None,
                "@" + name,
                name,
                SyntaxTriviaList.Empty
            )
        );
    }

    /// <summary>
    /// Returns <c>true</c> when inserting an <c>await</c> at <paramref name="node"/> would make a
    /// ref-like or <c>ref</c> local span that await, which does not compile (CS4007/CS8177).
    /// </summary>
    /// <remarks>
    /// Since C# 13 an async method may declare a <c>Span&lt;T&gt;</c> or other <c>ref struct</c>
    /// local, provided its lifetime does not cross an <c>await</c>. Code that satisfies that today
    /// can be broken by a rewrite that introduces a new await between the local's declaration and a
    /// later use — the call binds perfectly well, so only a lifetime check catches it. Approximated
    /// conservatively: any such local declared before the node and read after it withholds the fix.
    /// </remarks>
    public static bool AwaitWouldSpanRefLikeLocal(
        SemanticModel semanticModel,
        SyntaxNode node,
        int? awaitPosition = null,
        int? declarationCutoff = null
    )
    {
        // The node's *end*, not its start: a rewrite like `Thread.Sleep(span[0])` ->
        // `await Task.Delay(span[0])` evaluates its arguments before suspending, so a ref-like value
        // used only inside the call is already consumed. Callers whose await lands elsewhere —
        // `using` -> `await using`, which awaits at scope exit — say so explicitly.
        var position = awaitPosition ?? node.Span.End;

        // Which declarations can still be live at that await. For an ordinary rewrite it is
        // everything declared before it; for `await using` it is everything declared before the
        // *using*, because declarations dispose in reverse order — a later ref struct has already
        // been disposed by the time this one's disposal awaits.
        var cutoff = declarationCutoff ?? position;

        var body = node.Ancestors()
            .FirstOrDefault(a =>
                a
                    is BaseMethodDeclarationSyntax
                        or LocalFunctionStatementSyntax
                        or AnonymousFunctionExpressionSyntax
                        or AccessorDeclarationSyntax
            )
            // A top-level program has no declared body; its global statements are the synthesized
            // entry point, and locals declared there have the same lifetimes.
            ?? node.Ancestors().OfType<CompilationUnitSyntax>().FirstOrDefault();
        if (body is null)
            return false;

        // A `foreach` over a ref-like collection holds an enumerator that stays live for the whole
        // body, even though the collection identifier only appears in the header. Its lifetime is
        // implicit, so a use-site scan never sees it.
        for (var current = node.Parent; current != null && current != body; current = current.Parent)
        {
            if (
                current.Parent is CommonForEachStatementSyntax forEach
                && forEach.Statement == current
            )
            {
                // The enumerator is what stays live, and it can be a ref struct even when the
                // collection is an ordinary reference type — so ask for the bound enumerator rather
                // than reading the collection expression's type.
                var enumerator = semanticModel
                    .GetForEachStatementInfo(forEach)
                    .GetEnumeratorMethod?.ReturnType;
                if (
                    enumerator?.IsRefLikeType == true
                    || semanticModel.GetTypeInfo(forEach.Expression).Type?.IsRefLikeType == true
                )
                    return true;

                // `foreach (ref int item in …)` binds a ref local that cannot survive an await
                // (CS9217). It is not a VariableDeclarator, so the declarator scan below never sees
                // it, and neither the collection nor the enumerator need be ref-like.
                if (
                    forEach is ForEachStatementSyntax simpleForEach
                    && semanticModel.GetDeclaredSymbol(simpleForEach) is ILocalSymbol iteration
                    && (iteration.RefKind != RefKind.None || iteration.Type.IsRefLikeType)
                )
                    return true;
            }

            // A ref-like resource in a `using` statement is disposed at the end of the block, so it
            // is live across everything inside it.
            if (
                current.Parent is UsingStatementSyntax usingStatement
                && usingStatement.Statement == current
                && UsingResourceIsRefLike(semanticModel, usingStatement)
            )
                return true;
        }

        // Both declaration forms: `Span<int> s = …` and a designation such as `out Span<int> s`,
        // a pattern, or a deconstruction — all introduce a local whose lifetime matters here.
        var declarations = body.DescendantNodes()
            .Where(candidate =>
                candidate is VariableDeclaratorSyntax or SingleVariableDesignationSyntax
            );

        foreach (var declaration in declarations)
        {
            if (declaration.SpanStart >= cutoff)
                continue;

            if (
                semanticModel.GetDeclaredSymbol(declaration) is not ILocalSymbol local
                || (!local.Type.IsRefLikeType && local.RefKind == RefKind.None)
            )
                continue;

            // A local declared in a sibling or already-closed block is out of scope by the time the
            // call runs, so nothing it does can span the inserted await.
            // End-inclusive: an await that runs at scope exit (disposal) sits exactly at
            // scope.Span.End, which TextSpan.Contains treats as outside — and that would skip every
            // local in the scope, quietly disabling the guard for the case it was written for.
            var scope = DeclarationScopeOf(declaration);
            if (scope is null || position < scope.Span.Start || position > scope.Span.End)
                continue;

            // A `using var` local is disposed at scope exit, so it is live past the call whether or
            // not the identifier appears again — the implicit Dispose is the later use.
            if (
                declaration.Parent?.Parent is LocalDeclarationStatementSyntax localDeclaration
                && localDeclaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
            )
                return true;

            if (
                ReferencesLocal(
                    semanticModel,
                    body,
                    local,
                    reference => reference.SpanStart > position
                )
            )
                return true;

            // A backward `goto` is a loop the syntax does not show: control returns to a label
            // before the call, so a reference between the label and the jump runs again after the
            // inserted await.
            foreach (var jump in body.DescendantNodes().OfType<GotoStatementSyntax>())
            {
                if (jump.Expression is not IdentifierNameSyntax labelName)
                    continue;

                var label = body.DescendantNodes()
                    .OfType<LabeledStatementSyntax>()
                    .FirstOrDefault(candidate =>
                        candidate.Identifier.Text == labelName.Identifier.Text
                    );

                if (
                    label is null
                    || label.SpanStart > jump.SpanStart
                    || position < label.SpanStart
                    || position > jump.Span.End
                )
                    continue;

                if (
                    ReferencesLocal(
                        semanticModel,
                        body,
                        local,
                        reference =>
                            reference.SpanStart >= label.SpanStart
                            && reference.SpanStart <= jump.Span.End
                    )
                )
                    return true;
            }

            // Inside a loop, position is not execution order: a `for` condition or incrementor is
            // written before the body but runs again after it, so any reference within an enclosing
            // loop crosses the inserted await on the next iteration.
            for (var current = node.Parent; current != null && current != body; current = current.Parent)
            {
                if (
                    current
                        is ForStatementSyntax
                            or WhileStatementSyntax
                            or DoStatementSyntax
                            or CommonForEachStatementSyntax
                    && ReferencesLocal(
                        semanticModel,
                        body,
                        local,
                        reference => current.Span.Contains(reference.Span)
                    )
                )
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when a <c>using</c> statement's resource — declared or expression form —
    /// has a ref-like type.
    /// </summary>
    private static bool UsingResourceIsRefLike(
        SemanticModel semanticModel,
        UsingStatementSyntax usingStatement
    )
    {
        if (usingStatement.Declaration is { } declaration)
        {
            return declaration.Variables.Any(variable =>
                semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol local
                && local.Type.IsRefLikeType
            );
        }

        return usingStatement.Expression is { } expression
            && semanticModel.GetTypeInfo(expression).Type?.IsRefLikeType == true;
    }


    /// <summary>
    /// The syntax node bounding a local's scope: the enclosing block, <c>for</c> statement, or
    /// switch section.
    /// </summary>
    private static SyntaxNode? DeclarationScopeOf(SyntaxNode declaration) =>
        declaration
            .Ancestors()
            .FirstOrDefault(a =>
                // CompilationUnit, not GlobalStatement: in a top-level program every global
                // statement shares one scope, so stopping at the individual statement would make
                // each local look confined to its own declaration.
                a is BlockSyntax
                    or ForStatementSyntax
                    or SwitchSectionSyntax
                    or CompilationUnitSyntax
            );

    /// <summary>
    /// Returns <c>true</c> when <paramref name="local"/> is referenced anywhere in
    /// <paramref name="body"/> at a position satisfying <paramref name="predicate"/>.
    /// </summary>
    private static bool ReferencesLocal(
        SemanticModel semanticModel,
        SyntaxNode body,
        ILocalSymbol local,
        System.Func<IdentifierNameSyntax, bool> predicate
    ) =>
        body.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier =>
                identifier.Identifier.Text == local.Name
                && predicate(identifier)
                // `nameof(span)` never reads the value at runtime, so it does not keep the local
                // live across an await — the same compile-time-only distinction CC009/CC014/CC016
                // draw.
                && !IsInsideNameof(identifier, semanticModel, default)
                && SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier).Symbol,
                    local
                )
            );


    /// <summary>
    /// Property value used by every rule whose fix inserts an <c>await</c>, to record that the
    /// diagnostic stands but no compilable rewrite exists at this position.
    /// </summary>
    public const string AwaitUnsafeReason = "await-insertion-unsafe";

    /// <summary>
    /// Returns <c>true</c> when inserting an <c>await</c> at <paramref name="node"/> would not
    /// compile — either the syntax position forbids it, or it would cut across a ref-like lifetime.
    /// </summary>
    /// <remarks>
    /// The blocking call these rules flag is just as blocking in such a position; what is missing is
    /// a mechanical rewrite, so callers should report the diagnostic and withhold the fix rather than
    /// stay silent.
    /// </remarks>
    public static bool AwaitInsertionIsUnsafe(
        SemanticModel semanticModel,
        SyntaxNode node,
        int? awaitPosition = null,
        int? declarationCutoff = null
    ) =>
        AwaitIsForbiddenHere(node)
        || AwaitWouldSpanRefLikeLocal(semanticModel, node, awaitPosition, declarationCutoff)
        || RefLikeValuePendingAt(semanticModel, node);

    /// <summary>
    /// Returns <c>true</c> when a ref-like value is still pending on the stack at
    /// <paramref name="node"/>, so an <c>await</c> inserted there would strand it (CS4007).
    /// </summary>
    /// <remarks>
    /// Declared locals are only half the story: in <c>Consume(stackalloc int[1], task.Result)</c> the
    /// <c>Span&lt;int&gt;</c> never has a name, yet it is on the stack when the second argument is
    /// evaluated. What matters is a value that has been produced and not yet consumed — so this walks
    /// outward from the node and, at each level, asks only whether an <i>already-evaluated sibling</i>
    /// is itself ref-like. Nested subexpressions are deliberately not inspected:
    /// <c>Consume(Read(stackalloc int[1]), task.Result)</c> is safe because <c>Read</c> returns an
    /// <c>int</c> and the span it consumed is gone.
    /// </remarks>
    private static bool RefLikeValuePendingAt(SemanticModel semanticModel, SyntaxNode node)
    {
        for (var current = node; current.Parent != null; current = current.Parent)
        {
            // Boundaries of the expression being evaluated. Beyond these nothing is mid-evaluation,
            // and an expression-bodied member has no enclosing statement to stop at.
            if (
                current is StatementSyntax
                || current.Parent
                    is ArrowExpressionClauseSyntax
                        or AnonymousFunctionExpressionSyntax
                        or MemberDeclarationSyntax
                        or EqualsValueClauseSyntax
            )
                break;

            // The receiver of a call whose arguments contain the node: `span.Slice(task.Result)`
            // keeps the span pending while the argument is evaluated. It reaches PrecedingOperands
            // only as a method-group expression, whose type is null.
            if (
                current.Parent is InvocationExpressionSyntax enclosingCall
                && enclosingCall.Expression is MemberAccessExpressionSyntax callTarget
                && semanticModel.GetTypeInfo(callTarget.Expression).Type?.IsRefLikeType == true
            )
                return true;

            foreach (var sibling in PrecedingOperands(current))
            {
                if (IsPendingRefLike(semanticModel, sibling))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when an already-evaluated operand still holds something that cannot
    /// survive an <c>await</c>.
    /// </summary>
    /// <remarks>
    /// Three shapes qualify. The operand's own type is ref-like; or it occupies a
    /// <i>storage-preserving</i> position — an assignment target, or a <c>ref</c>/<c>out</c>/
    /// <c>in</c> argument — and refers into a ref-like receiver (<c>span[0] = …</c> yields an
    /// <c>int</c> while keeping the span pending); or it is a managed reference from a
    /// ref-returning member, which cannot cross an await either (CS8178). An ordinary value read
    /// through a ref-like receiver is <i>not</i> pending: <c>Consume(span[0], task.Result)</c>
    /// consumes the element before the await and rewrites safely.
    /// </remarks>
    private static bool IsPendingRefLike(SemanticModel semanticModel, PendingOperand operand)
    {
        var expression = operand.Expression;

        if (semanticModel.GetTypeInfo(expression).Type?.IsRefLikeType == true)
            return true;

        if (!operand.PreservesStorage)
            return false;

        var receiver = expression switch
        {
            ElementAccessExpressionSyntax elementAccess => elementAccess.Expression,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            _ => null,
        };

        if (
            receiver != null
            && semanticModel.GetTypeInfo(receiver).Type?.IsRefLikeType == true
        )
            return true;

        return semanticModel.GetSymbolInfo(expression).Symbol switch
        {
            IMethodSymbol method => method.RefKind != RefKind.None,
            IPropertySymbol property => property.RefKind != RefKind.None,
            ILocalSymbol local => local.RefKind != RefKind.None,
            _ => false,
        };
    }

    /// <summary>An operand evaluated before the insertion point, and how it is being used.</summary>
    private readonly struct PendingOperand
    {
        public PendingOperand(ExpressionSyntax expression, bool preservesStorage)
        {
            Expression = expression;
            PreservesStorage = preservesStorage;
        }

        public ExpressionSyntax Expression { get; }

        /// <summary>
        /// <c>true</c> when the operand denotes a location rather than a copied value — an
        /// assignment target or a <c>ref</c>/<c>out</c>/<c>in</c> argument.
        /// </summary>
        public bool PreservesStorage { get; }
    }

    /// <summary>
    /// The operands of <paramref name="node"/>'s parent that are fully evaluated before
    /// <paramref name="node"/> is, paired with whether each denotes a storage location.
    /// </summary>
    private static IEnumerable<PendingOperand> PrecedingOperands(SyntaxNode node)
    {
        if (node.Parent is null)
            yield break;

        // The arms of a conditional are mutually exclusive: whichever one contains the node, the
        // other never ran, so it holds nothing pending.
        if (node.Parent is ConditionalExpressionSyntax)
            yield break;

        foreach (var child in node.Parent.ChildNodes())
        {
            if (child == node || child.Span.End > node.SpanStart)
                continue;

            switch (child)
            {
                case ArgumentSyntax argument:
                    yield return new PendingOperand(
                        argument.Expression,
                        !argument.RefKindKeyword.IsKind(SyntaxKind.None)
                    );
                    break;

                case ExpressionSyntax value:
                    yield return new PendingOperand(
                        value,
                        node.Parent is AssignmentExpressionSyntax assignment
                            && assignment.Left == value
                    );
                    break;
            }
        }
    }
}
