using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects <c>async void</c> methods that are not event handlers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC023
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// An <c>async void</c> method cannot be awaited, so a caller cannot observe its completion, pass it
/// a <c>CancellationToken</c>-bound continuation, or catch its exceptions — an unhandled exception in
/// one crashes the process. Returning <c>Task</c> instead makes the operation awaitable and
/// cancellable. Event handlers are the one sanctioned use of <c>async void</c> and are excluded.
/// </para>
/// <para>
/// <b>What it detects:</b> an <c>async void</c> method whose signature is not the event-handler shape
/// (<c>(object sender, EventArgs e)</c>) and is not dictated by an override/interface/extern.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async void ProcessAsync()   // CC023 -> async Task ProcessAsync()
/// {
///     await DoWorkAsync();
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AsyncVoidAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC023";

    private static readonly LocalizableString Title = "Avoid async void";
    private static readonly LocalizableString MessageFormat = "Async method '{0}' returns void; return Task so it can be awaited";
    private static readonly LocalizableString Description = "async void methods cannot be awaited and their exceptions crash the process; return Task instead (except for event handlers).";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
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

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
    }

    private void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var local = (LocalFunctionStatementSyntax)context.Node;

        // A local function cannot be an event handler or an override/interface implementation, so
        // the only checks are the async modifier and the void return.
        if (!local.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;
        if (local.ReturnType is not PredefinedTypeSyntax predefined ||
            !predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, local.Identifier.GetLocation(), local.Identifier.Text));
    }

    private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;
        if (method.ReturnType is not PredefinedTypeSyntax predefined ||
            !predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
            return;

        if (context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is not IMethodSymbol symbol)
            return;

        // Event handlers are the sanctioned use of async void.
        if (IsEventHandlerShape(symbol))
            return;

        // The developer cannot change a signature dictated by an override/interface/extern.
        if (CancellationTokenHelpers.IsSignatureExternallyControlled(symbol))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, method.Identifier.GetLocation(), method.Identifier.Text));
    }

    private static bool IsEventHandlerShape(IMethodSymbol method)
    {
        if (method.Parameters.Length != 2)
            return false;

        if (method.Parameters[0].Type.SpecialType != SpecialType.System_Object)
            return false;

        for (var type = method.Parameters[1].Type; type != null; type = type.BaseType)
        {
            if (type.Name == "EventArgs" && type.ContainingNamespace?.ToDisplayString() == "System")
                return true;
        }

        return false;
    }
}
