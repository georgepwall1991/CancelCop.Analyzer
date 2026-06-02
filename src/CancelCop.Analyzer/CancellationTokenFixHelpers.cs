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
    /// Returns a parameter name for the injected token that does not collide with an
    /// existing parameter name (guards against CS0100 duplicate-parameter errors).
    /// </summary>
    public static string GetUniqueTokenParameterName(ParameterListSyntax parameterList)
    {
        var existing = parameterList.Parameters
            .Select(p => p.Identifier.Text)
            .ToImmutableHashSet();

        if (!existing.Contains(DefaultTokenName))
            return DefaultTokenName;

        if (!existing.Contains("ct"))
            return "ct";

        var suffix = 2;
        while (existing.Contains(DefaultTokenName + suffix))
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
    /// Adds <c>using System.Threading;</c> in alphabetical order if it is not already present,
    /// preserving the file's leading trivia and avoiding spurious blank lines between usings.
    /// </summary>
    public static CompilationUnitSyntax AddSystemThreadingUsing(CompilationUnitSyntax compilationUnit)
    {
        if (compilationUnit.Usings.Any(u => u.Name?.ToString() == SystemThreadingNamespace))
            return compilationUnit;

        var usings = compilationUnit.Usings.ToList();
        var newLine = SyntaxFactory.EndOfLine(DetectNewLine(compilationUnit));
        var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(SystemThreadingNamespace));

        if (usings.Count == 0)
        {
            // No existing usings: place the directive at the top with a trailing newline.
            usings.Add(newUsing.WithTrailingTrivia(newLine));
            return compilationUnit.WithUsings(SyntaxFactory.List(usings));
        }

        // Alphabetical (ordinal) insert position.
        var insertIndex = usings.Count;
        for (var i = 0; i < usings.Count; i++)
        {
            if (string.CompareOrdinal(SystemThreadingNamespace, usings[i].Name?.ToString()) < 0)
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
            // Append after the last using: match its trivia shape.
            var last = usings[usings.Count - 1];
            newUsing = newUsing
                .WithLeadingTrivia(last.GetLeadingTrivia())
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
