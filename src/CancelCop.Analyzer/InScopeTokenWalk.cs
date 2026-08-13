using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CancelCop.Analyzer;

/// <summary>
/// Shared in-scope token resolution: <c>CancellationToken</c> parameters first, then proven
/// framework properties (<c>HttpContext.RequestAborted</c>, <c>ServerCallContext.CancellationToken</c>).
/// Isolated so mutation testing can target this surface without the rest of
/// <see cref="CancellationTokenHelpers"/>.
/// </summary>
internal static class InScopeTokenWalk
{
    public static InScopeToken? Find(SyntaxNode node, SemanticModel semanticModel)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is LocalFunctionStatementSyntax localFunction)
            {
                var token = TokenFromMethod(
                    semanticModel.GetDeclaredSymbol(localFunction) as IMethodSymbol
                );
                if (token != null)
                    return token;
                if (localFunction.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return null;
            }
            else if (current is AnonymousFunctionExpressionSyntax anonymousFunction)
            {
                var token = TokenFromMethod(
                    semanticModel.GetSymbolInfo(anonymousFunction).Symbol as IMethodSymbol
                );
                if (token != null)
                    return token;
                if (anonymousFunction.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return null;
            }
            else if (current is ConstructorDeclarationSyntax constructor)
            {
                return TokenFromMethod(
                    semanticModel.GetDeclaredSymbol(constructor) as IMethodSymbol
                );
            }
            else if (current is MethodDeclarationSyntax method)
            {
                var token = TokenFromMethod(
                    semanticModel.GetDeclaredSymbol(method) as IMethodSymbol
                );
                if (token != null)
                    return token;
                if (method.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return null;
            }
            else if (current is OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax)
            {
                return null;
            }
            else if (current is BaseFieldDeclarationSyntax field)
            {
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
                return FindPrimaryConstructorToken(typeDeclaration, semanticModel);
            }

            current = current.Parent;
        }

        return null;
    }

    public static InScopeToken? TokenFromMethod(IMethodSymbol? method)
    {
        if (method == null)
            return null;

        var parameter = CancellationTokenHelpers.FindCancellationTokenParameter(method);
        if (parameter != null)
            return InScopeToken.FromParameter(parameter);

        return FindFrameworkToken(method.Parameters);
    }

    public static InScopeToken? FindFrameworkToken(IEnumerable<IParameterSymbol> parameters)
    {
        IParameterSymbol? grpcContext = null;

        foreach (var parameter in parameters)
        {
            if (IsHttpContext(parameter.Type))
            {
                var aborted = FindCancellationTokenProperty(parameter.Type, "RequestAborted");
                if (aborted != null)
                    return InScopeToken.FromMember(parameter, aborted);
            }
            else if (grpcContext == null && IsServerCallContext(parameter.Type))
            {
                grpcContext = parameter;
            }
        }

        if (grpcContext != null)
        {
            var grpcToken = FindCancellationTokenProperty(grpcContext.Type, "CancellationToken");
            if (grpcToken != null)
                return InScopeToken.FromMember(grpcContext, grpcToken);
        }

        return null;
    }

    public static bool IsHttpContext(ITypeSymbol type) =>
        type.Name == "HttpContext"
        && type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Http";

    public static bool IsServerCallContext(ITypeSymbol type) =>
        type.Name == "ServerCallContext"
        && type.ContainingNamespace?.ToDisplayString() == "Grpc.Core";

    public static IPropertySymbol? FindCancellationTokenProperty(ITypeSymbol type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (
                    member is IPropertySymbol { IsStatic: false } property
                    && CancellationTokenHelpers.IsCancellationToken(property.Type)
                )
                {
                    return property;
                }
            }
        }

        return null;
    }

    public static bool IsConventionMiddlewareEntryPoint(IMethodSymbol method)
    {
        if (method.IsStatic)
            return false;
        if (method.DeclaredAccessibility != Accessibility.Public)
            return false;
        if (method.Name is not ("Invoke" or "InvokeAsync"))
            return false;
        if (method.Parameters.Length == 0)
            return false;

        return IsHttpContext(method.Parameters[0].Type);
    }

    private static InScopeToken? FindPrimaryConstructorToken(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel semanticModel
    )
    {
        if (typeDeclaration.ParameterList != null)
        {
            var parameters = new List<IParameterSymbol>();
            foreach (var parameter in typeDeclaration.ParameterList.Parameters)
            {
                if (semanticModel.GetDeclaredSymbol(parameter) is IParameterSymbol parameterSymbol)
                    parameters.Add(parameterSymbol);
            }

            var tokenParameter = parameters.FirstOrDefault(p =>
                CancellationTokenHelpers.IsCancellationToken(p.Type)
            );
            if (tokenParameter != null)
                return InScopeToken.FromParameter(tokenParameter);

            return FindFrameworkToken(parameters);
        }

        if (semanticModel.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol typeSymbol)
        {
            foreach (var constructor in typeSymbol.InstanceConstructors)
            {
                foreach (var reference in constructor.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax() is TypeDeclarationSyntax)
                        return TokenFromMethod(constructor);
                }
            }
        }

        return null;
    }
}
