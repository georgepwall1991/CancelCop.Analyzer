using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects blocking calls on async operations (Task.Wait(), .Result, GetAwaiter().GetResult()).
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC011
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Blocking on async code can cause deadlocks in UI applications and ASP.NET contexts.
/// It defeats the purpose of async, blocks threads, reduces scalability, and makes
/// cancellation ineffective. Use await instead.
/// </para>
/// <para>
/// <b>What it detects:</b>
/// <list type="bullet">
/// <item>Task.Wait() and Task.WaitAll() calls</item>
/// <item>Accessing .Result property on Task or Task&lt;T&gt;</item>
/// <item>GetAwaiter().GetResult() pattern</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Violation:
/// public void Process()
/// {
///     var result = GetDataAsync().Result;  // CC011
///     GetDataAsync().Wait();               // CC011
/// }
///
/// // Fixed:
/// public async Task ProcessAsync()
/// {
///     var result = await GetDataAsync();
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingCallAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC011";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Avoid blocking on async code";
    private static readonly LocalizableString MessageFormat = "Avoid using '{0}' - use await instead";
    private static readonly LocalizableString Description = "Blocking on async code with .Wait(), .Result, or .GetAwaiter().GetResult() can cause deadlocks. Use await instead.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
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
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // Check for .Result property access
        if (memberAccess.Name.Identifier.Text == "Result")
        {
            var expressionType = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (IsTaskType(expressionType))
            {
                // Skip if already inside a GetAwaiter().GetResult() pattern (we'll catch that separately)
                if (IsPartOfGetAwaiterGetResult(memberAccess))
                    return;

                var diagnostic = Diagnostic.Create(
                    Rule,
                    memberAccess.Name.GetLocation(),
                    ".Result");

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.Text;

            // Check for .Wait() call
            if (memberName == "Wait")
            {
                var expressionType = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
                if (IsTaskType(expressionType))
                {
                    var diagnostic = Diagnostic.Create(
                        Rule,
                        memberAccess.Name.GetLocation(),
                        ".Wait()");

                    context.ReportDiagnostic(diagnostic);
                    return;
                }
            }

            // Check for .GetResult() after GetAwaiter()
            if (memberName == "GetResult")
            {
                if (memberAccess.Expression is InvocationExpressionSyntax innerInvocation &&
                    innerInvocation.Expression is MemberAccessExpressionSyntax innerMemberAccess &&
                    innerMemberAccess.Name.Identifier.Text == "GetAwaiter")
                {
                    var expressionType = context.SemanticModel.GetTypeInfo(innerMemberAccess.Expression).Type;
                    if (IsTaskType(expressionType))
                    {
                        var diagnostic = Diagnostic.Create(
                            Rule,
                            invocation.GetLocation(),
                            ".GetAwaiter().GetResult()");

                        context.ReportDiagnostic(diagnostic);
                        return;
                    }
                }
            }

            // Check for Task.WaitAll()
            if (memberName == "WaitAll" || memberName == "WaitAny")
            {
                var symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (symbol?.ContainingType?.Name == "Task" &&
                    symbol.ContainingType.ContainingNamespace?.ToString() == "System.Threading.Tasks")
                {
                    var diagnostic = Diagnostic.Create(
                        Rule,
                        memberAccess.Name.GetLocation(),
                        $".{memberName}()");

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static bool IsTaskType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        var typeName = type.Name;
        var ns = type.ContainingNamespace?.ToString();

        // Check for Task, Task<T>, ValueTask, or ValueTask<T>
        if (ns == "System.Threading.Tasks")
        {
            if (typeName == "Task" || typeName == "ValueTask")
                return true;
        }

        return false;
    }

    private static bool IsPartOfGetAwaiterGetResult(MemberAccessExpressionSyntax memberAccess)
    {
        // Check if this .Result is part of a larger GetAwaiter().GetResult() pattern
        // which would be reported separately with a more specific message
        var parent = memberAccess.Parent;

        if (parent is MemberAccessExpressionSyntax parentMemberAccess &&
            parentMemberAccess.Name.Identifier.Text == "GetAwaiter")
        {
            return true;
        }

        return false;
    }
}
