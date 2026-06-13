using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a public async SignalR <c>Hub</c> method without a
/// <c>CancellationToken</c> parameter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC018
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// SignalR binds a <c>CancellationToken</c> parameter on a hub method to the invocation's abort
/// token, which is signalled when the client disconnects or the connection is aborted. A long-
/// running hub method without one keeps running after the caller is gone. This is the SignalR
/// analogue of CC005B (controller actions).
/// </para>
/// <para>
/// <b>What it detects:</b> a public, non-static, async (or <c>Task</c>/<c>ValueTask</c>-returning)
/// method on a type deriving from <c>Microsoft.AspNetCore.SignalR.Hub</c> / <c>Hub&lt;T&gt;</c> that
/// has no <c>CancellationToken</c> parameter. Hub lifecycle overrides
/// (<c>OnConnectedAsync</c>/<c>OnDisconnectedAsync</c>) and other externally-controlled signatures
/// are excluded.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class ChatHub : Hub
/// {
///     public async Task Broadcast(string message)   // CC018
///     {
///         await Clients.All.SendAsync("recv", message);
///     }
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SignalRHubAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC018";

    private static readonly LocalizableString Title = "SignalR hub method should have CancellationToken parameter";
    private static readonly LocalizableString MessageFormat = "SignalR hub method '{0}' should have a CancellationToken parameter";
    private static readonly LocalizableString Description = "SignalR hub methods should accept a CancellationToken, which SignalR binds to the invocation/connection abort token.";
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
    }

    private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(methodDeclaration, context.CancellationToken) is not IMethodSymbol method)
            return;

        // Only public instance methods are invokable hub methods.
        if (method.DeclaredAccessibility != Accessibility.Public || method.IsStatic)
            return;
        if (method.MethodKind != MethodKind.Ordinary)
            return;

        if (!method.IsAsync && !CancellationTokenHelpers.IsAsyncReturnType(method.ReturnType))
            return;

        // Lifecycle overrides (OnConnectedAsync/OnDisconnectedAsync) and other dictated signatures
        // cannot take an extra token.
        if (CancellationTokenHelpers.IsSignatureExternallyControlled(method))
            return;

        if (!DerivesFromHub(method.ContainingType))
            return;

        if (CancellationTokenHelpers.HasCancellationTokenParameter(method))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, methodDeclaration.Identifier.GetLocation(), method.Name));
    }

    private static bool DerivesFromHub(INamedTypeSymbol? type)
    {
        for (var current = type?.BaseType; current != null; current = current.BaseType)
        {
            // Hub and the generic Hub<T> both have metadata name "Hub".
            if (current.Name == "Hub" &&
                current.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.SignalR")
                return true;
        }

        return false;
    }
}
