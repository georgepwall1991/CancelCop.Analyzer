using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CancelCop.Analyzer;

/// <summary>
/// Shared syntax-rewriting helpers used by the code-fix providers to add a
/// <c>CancellationToken</c> parameter (or argument) without producing uncompilable code.
/// </summary>
internal static class CancellationTokenFixHelpers
{
    private const string DefaultTokenName = "cancellationToken";
    private const string SystemThreadingNamespace = "System.Threading";

    /// <summary>
    /// Returns a parameter name for the injected token that does not collide with an existing
    /// parameter name (CS0100) nor with any identifier declared in the supplied body scope
    /// (CS0136 — a parameter may not share a name with a local in the same method/lambda).
    /// </summary>
    public static string GetUniqueTokenParameterName(ParameterListSyntax parameterList, SyntaxNode? bodyScope = null)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in parameterList.Parameters)
            reserved.Add(parameter.Identifier.Text);

        if (bodyScope != null)
        {
            // Conservatively reserve every identifier that textually appears in the body so the
            // injected parameter cannot shadow a local/range-variable/etc. declared there.
            foreach (var token in bodyScope.DescendantTokens())
            {
                if (token.IsKind(SyntaxKind.IdentifierToken))
                    reserved.Add(token.Text);
            }
        }

        if (!reserved.Contains(DefaultTokenName))
            return DefaultTokenName;

        if (!reserved.Contains("ct"))
            return "ct";

        var suffix = 2;
        while (reserved.Contains(DefaultTokenName + suffix))
            suffix++;

        return DefaultTokenName + suffix;
    }

    /// <summary>
    /// Inserts the token parameter before any trailing <c>params</c> parameter (which must
    /// remain last, guarding against CS0231); otherwise appends it.
    /// </summary>
    public static ParameterListSyntax InsertTokenParameter(
        ParameterListSyntax parameterList,
        ParameterSyntax tokenParameter)
    {
        var parameters = parameterList.Parameters;
        var insertIndex = parameters.Count;

        if (parameters.Count > 0 &&
            parameters[parameters.Count - 1].Modifiers.Any(SyntaxKind.ParamsKeyword))
        {
            insertIndex = parameters.Count - 1;
        }

        return parameterList.WithParameters(parameters.Insert(insertIndex, tokenParameter));
    }

    /// <summary>
    /// Returns the index at which <see cref="InsertTokenParameter"/> will place the token
    /// (before any trailing <c>params</c> parameter, otherwise the end).
    /// </summary>
    private static int GetTokenInsertIndex(ParameterListSyntax parameterList)
    {
        var parameters = parameterList.Parameters;
        if (parameters.Count > 0 &&
            parameters[parameters.Count - 1].Modifiers.Any(SyntaxKind.ParamsKeyword))
        {
            return parameters.Count - 1;
        }

        return parameters.Count;
    }

    /// <summary>
    /// Returns <c>true</c> when an inserted required token would follow an optional parameter,
    /// which the compiler rejects (CS1737). In that case the token must be given a default value.
    /// </summary>
    public static bool RequiresDefaultToAppend(ParameterListSyntax parameterList)
    {
        var insertIndex = GetTokenInsertIndex(parameterList);
        return parameterList.Parameters.Take(insertIndex).Any(p => p.Default != null);
    }

    /// <summary>
    /// Builds an <c>= default</c> clause for a <c>CancellationToken</c> parameter.
    /// </summary>
    public static EqualsValueClauseSyntax DefaultValueClause() =>
        SyntaxFactory.EqualsValueClause(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.DefaultLiteralExpression,
                SyntaxFactory.Token(SyntaxKind.DefaultKeyword)));

    /// <summary>
    /// Appends a <c>CancellationToken</c> argument to the invocation's argument list. When the
    /// call already uses any named argument, the token is appended as a named argument
    /// (<c>name: token</c>) — a trailing positional argument after an out-of-position named
    /// argument is CS8323 — otherwise it stays positional.
    /// </summary>
    public static ArgumentListSyntax AddTokenArgument(
        ArgumentListSyntax argumentList,
        string tokenExpression,
        string? namedParameterName)
    {
        var tokenArgument = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(tokenExpression));

        if (namedParameterName != null &&
            argumentList.Arguments.Any(a => a.NameColon != null))
        {
            tokenArgument = tokenArgument.WithNameColon(
                SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(namedParameterName)));
        }

        return argumentList.AddArguments(tokenArgument);
    }

    /// <summary>
    /// Adds <c>using System.Threading;</c> in alphabetical order if it is not already present,
    /// preserving the file's leading trivia and avoiding spurious blank lines between usings.
    /// </summary>
    public static CompilationUnitSyntax AddSystemThreadingUsing(CompilationUnitSyntax compilationUnit) =>
        AddUsing(compilationUnit, SystemThreadingNamespace);

    /// <summary>
    /// Adds <c>using <paramref name="namespaceName"/>;</c> in alphabetical order if it is not
    /// already present as a plain (non-alias, non-static) import, preserving the file's leading
    /// trivia and avoiding spurious blank lines between usings.
    /// </summary>
    public static CompilationUnitSyntax AddUsing(CompilationUnitSyntax compilationUnit, string namespaceName)
    {
        // Only a plain 'using <namespaceName>;' makes the unqualified type resolve. An alias
        // ('using X = <namespaceName>;') or static using does not, so those must NOT
        // short-circuit insertion (otherwise the fixed code is CS0246).
        var alreadyImported = compilationUnit.Usings.Any(u =>
            u.Alias == null &&
            !u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) &&
            u.Name?.ToString() == namespaceName);

        if (alreadyImported)
            return compilationUnit;

        var usings = compilationUnit.Usings.ToList();
        var newLine = SyntaxFactory.EndOfLine(DetectNewLine(compilationUnit));
        var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName));

        if (usings.Count == 0)
        {
            // No existing usings: place the directive at the top with a trailing newline.
            usings.Add(newUsing.WithTrailingTrivia(newLine));
            return compilationUnit.WithUsings(SyntaxFactory.List(usings));
        }

        // Global usings must precede all non-global usings in a file (CS8915), so the
        // directive can only be inserted at or after the end of the global-using block.
        var firstNonGlobal = 0;
        while (firstNonGlobal < usings.Count &&
               usings[firstNonGlobal].GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
        {
            firstNonGlobal++;
        }

        // Alphabetical (ordinal) insert position among the non-global usings.
        var insertIndex = usings.Count;
        for (var i = firstNonGlobal; i < usings.Count; i++)
        {
            if (string.CompareOrdinal(namespaceName, usings[i].Name?.ToString()) < 0)
            {
                insertIndex = i;
                break;
            }
        }

        if (insertIndex < usings.Count)
        {
            // Insert before an existing using: inherit its leading trivia (so the file's leading
            // blank lines stay in front of the using block) and strip that trivia from the displaced
            // using so no blank line is introduced between the two directives.
            var displaced = usings[insertIndex];
            newUsing = newUsing
                .WithLeadingTrivia(displaced.GetLeadingTrivia())
                .WithTrailingTrivia(newLine);
            usings[insertIndex] = displaced.WithLeadingTrivia();
        }
        else
        {
            // Append after the last using on its own line. The line break before it already comes
            // from the previous using's trailing newline, so only the last using's *indentation*
            // (its leading trivia minus end-of-line trivia) is carried over — copying its full
            // leading trivia would re-insert the file's leading newline and leave a blank line
            // between the two directives.
            var last = usings[usings.Count - 1];
            var indent = SyntaxFactory.TriviaList(
                last.GetLeadingTrivia().Where(t => !t.IsKind(SyntaxKind.EndOfLineTrivia)));
            newUsing = newUsing
                .WithLeadingTrivia(indent)
                .WithTrailingTrivia(last.GetTrailingTrivia());
        }

        usings.Insert(insertIndex, newUsing);
        return compilationUnit.WithUsings(SyntaxFactory.List(usings));
    }

    private static string DetectNewLine(SyntaxNode node)
    {
        foreach (var trivia in node.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                return trivia.ToString();
        }

        return "\n";
    }
}
