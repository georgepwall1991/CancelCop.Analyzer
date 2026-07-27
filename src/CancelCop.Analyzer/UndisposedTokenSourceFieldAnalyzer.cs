using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a <c>CancellationTokenSource</c> field created by its declaring type and
/// never disposed by it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC033
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// A <c>CancellationTokenSource</c> owns unmanaged-adjacent state — a timer when a delay is set, and
/// a registration list that every linked token and every <c>Register</c> callback adds to. A field
/// keeps that alive for the whole lifetime of the owning object, so a source that is never disposed
/// leaks for as long as its owner does. Linked sources are worse: an undisposed child stays attached
/// to its parent's callback list, so a long-lived parent accumulates every child ever created.
/// </para>
/// <para>
/// <b>How this differs from CC014:</b> CC014 covers a <i>local</i> source, where the fix is
/// mechanical — make it a <c>using</c> declaration. A field's lifetime is the object's, so the
/// resolution is to implement <c>IDisposable</c> (or dispose it in the existing one) and have the
/// owner's own disposal cascade. That is a design change, so CC033 is analyzer-only, like CC017,
/// CC020, CC024, CC027, CC031, and CC032.
/// </para>
/// <para>
/// <b>Conservative by design:</b> the rule only fires when the declaring type <i>creates</i> the
/// source — an injected or assigned-from-elsewhere source is owned by whoever created it, and
/// disposing it would be a bug. It stays quiet if any member disposes the field, if the field
/// escapes (returned, or passed as an argument, so something else may own it), and for
/// <c>static</c> fields, whose lifetime is the process and which are typically deliberate.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class Worker
/// {
///     // CC033: created here, never disposed — the timer and registration list outlive their use
///     private readonly CancellationTokenSource _cts = new CancellationTokenSource();
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UndisposedTokenSourceFieldAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC033";

    private static readonly LocalizableString Title =
        "CancellationTokenSource field is never disposed";
    private static readonly LocalizableString MessageFormat =
        "Field '{0}' creates a CancellationTokenSource that is never disposed";
    private static readonly LocalizableString Description =
        "A CancellationTokenSource field owns a timer and a registration list for the lifetime of its owner; if the declaring type creates it, the type should dispose it.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // A symbol-start action is what makes "never disposed" answerable without reaching for
        // Compilation.GetSemanticModel (RS1030): the nested node actions each arrive with the model
        // for their own tree, facts accumulate across every member and every partial declaration,
        // and the symbol-end action reports once the whole type has been seen.
        context.RegisterSymbolStartAction(OnTypeStart, SymbolKind.NamedType);
    }

    private static void OnTypeStart(SymbolStartAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        var candidates = type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field =>
                !field.IsStatic
                && !field.IsImplicitlyDeclared
                && IsCancellationTokenSource(field.Type)
            )
            .ToImmutableArray();

        if (candidates.IsEmpty)
            return;

        // Concurrent because node actions for different members may run in parallel.
        var created = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);
        var exonerated = new ConcurrentDictionary<IFieldSymbol, bool>(
            SymbolEqualityComparer.Default
        );

        context.RegisterSyntaxNodeAction(
            nodeContext => RecordCreation(nodeContext, candidates, created),
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression,
            SyntaxKind.InvocationExpression
        );

        context.RegisterSyntaxNodeAction(
            nodeContext => RecordDisposalOrEscape(nodeContext, candidates, exonerated),
            SyntaxKind.IdentifierName
        );

        context.RegisterSymbolEndAction(endContext =>
        {
            foreach (var field in candidates)
            {
                if (created.ContainsKey(field) && !exonerated.ContainsKey(field))
                    endContext.ReportDiagnostic(
                        Diagnostic.Create(Rule, field.Locations[0], field.Name)
                    );
            }
        });
    }

    /// <summary>
    /// Records that the declaring type constructs the source itself — the only case in which it owns
    /// the source and is responsible for disposing it.
    /// </summary>
    private static void RecordCreation(
        SyntaxNodeAnalysisContext context,
        ImmutableArray<IFieldSymbol> candidates,
        ConcurrentDictionary<IFieldSymbol, bool> created
    )
    {
        var expression = (ExpressionSyntax)context.Node;

        if (expression is InvocationExpressionSyntax invocation)
        {
            // CreateLinkedTokenSource is a factory rather than a constructor, and its result is
            // owned by the caller in exactly the same way.
            if (
                context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                    is not IMethodSymbol { Name: "CreateLinkedTokenSource" } factory
                || !IsCancellationTokenSource(factory.ContainingType)
            )
                return;
        }
        else if (
            context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type
                is not { } createdType
            || !IsCancellationTokenSource(createdType)
        )
            return;

        foreach (var field in candidates)
        {
            if (AssignsTo(expression, field, context))
                created[field] = true;
        }
    }

    /// <summary>
    /// Records that the type disposes the field, or lets it escape so that something else may own it.
    /// </summary>
    /// <remarks>
    /// Escape is treated as exoneration rather than as a finding: once the source is handed out, who
    /// disposes it is no longer decidable from this type alone, and guessing would produce noise.
    /// This mirrors CC014's conservative escape analysis.
    /// </remarks>
    private static void RecordDisposalOrEscape(
        SyntaxNodeAnalysisContext context,
        ImmutableArray<IFieldSymbol> candidates,
        ConcurrentDictionary<IFieldSymbol, bool> exonerated
    )
    {
        var identifier = (IdentifierNameSyntax)context.Node;

        var field = candidates.FirstOrDefault(candidate =>
            candidate.Name == identifier.Identifier.Text
        );
        if (field is null)
            return;

        if (
            !SymbolEqualityComparer.Default.Equals(
                context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol,
                field
            )
        )
            return;

        // The expression that denotes the field, which is not always the identifier: `this._cts`
        // and `Owner._cts` put the identifier in the *name* position of a member access, so
        // reading its immediate parent would look at the access instead of past it.
        var reference = UnwrapCompileTimeWrappers(
            identifier.Parent is MemberAccessExpressionSyntax qualified
            && qualified.Name == identifier
                ? (ExpressionSyntax)qualified
                : identifier
        );

        if (IsDisposeInvocation(reference, context))
        {
            exonerated[field] = true;
            return;
        }

        // `using (_cts) { }` disposes at the end of the block just as deterministically as an
        // explicit call.
        if (reference.Parent is UsingStatementSyntax usingStatement && usingStatement.Expression == reference)
        {
            exonerated[field] = true;
            return;
        }

        // Returned, passed to something that may take ownership, or copied into another location —
        // including a local alias, which disposal routinely goes through
        // (`var source = _cts; source.Dispose();`).
        var escaping = FollowValueForwarding(reference);
        if (
            escaping.Parent
                is ArgumentSyntax
                    or ReturnStatementSyntax
                    or ArrowExpressionClauseSyntax
                    or EqualsValueClauseSyntax
            || (
                escaping.Parent is AssignmentExpressionSyntax assignment
                && assignment.Right == escaping
            )
        )
            exonerated[field] = true;
    }

    /// <summary>
    /// Walks outward through expressions that pass their operand's value straight through, so an
    /// escape is recognised however the value reaches the exit.
    /// </summary>
    /// <remarks>
    /// <c>return expose ? _cts : null;</c> escapes exactly as much as <c>return _cts;</c>, but the
    /// reference's immediate parent is the conditional rather than the return.
    /// </remarks>
    private static ExpressionSyntax FollowValueForwarding(ExpressionSyntax expression)
    {
        var value = expression;
        while (true)
        {
            switch (value.Parent)
            {
                case ConditionalExpressionSyntax conditional
                    when conditional.WhenTrue == value || conditional.WhenFalse == value:
                    value = conditional;
                    continue;
                case BinaryExpressionSyntax coalesce
                    when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                    value = coalesce;
                    continue;
                case SwitchExpressionArmSyntax { Expression: var armValue } arm
                    when armValue == value && arm.Parent is SwitchExpressionSyntax switchExpression:
                    value = switchExpression;
                    continue;
                default:
                    return value;
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="expression"/> is the value given to
    /// <paramref name="field"/>, either in its initializer or by an assignment.
    /// </summary>
    private static bool AssignsTo(
        ExpressionSyntax expression,
        IFieldSymbol field,
        SyntaxNodeAnalysisContext context
    )
    {
        // Forwarding is followed here for the same reason it is when detecting escape:
        // `_cts = enabled ? new() : new()` assigns each branch's source to the field, but each
        // creation's immediate parent is the conditional.
        var parent = FollowValueForwarding(UnwrapCompileTimeWrappers(expression)).Parent;

        if (parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator })
        {
            return SymbolEqualityComparer.Default.Equals(
                context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken),
                field
            );
        }

        return parent is AssignmentExpressionSyntax assignment
            && assignment.Right == FollowValueForwarding(UnwrapCompileTimeWrappers(expression))
            && SymbolEqualityComparer.Default.Equals(
                context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken)
                    .Symbol,
                field
            );
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="reference"/> is the receiver of an actual
    /// <c>Dispose()</c>/<c>DisposeAsync()</c> call, written directly or through <c>?.</c>.
    /// </summary>
    /// <remarks>
    /// The invocation matters: <c>Action cleanup = _cts.Dispose;</c> names the method without ever
    /// calling it, so accepting a bare member access would exonerate a real leak.
    /// </remarks>
    private static bool IsDisposeInvocation(
        ExpressionSyntax reference,
        SyntaxNodeAnalysisContext context
    )
    {
        if (
            reference.Parent is MemberAccessExpressionSyntax access
            && access.Expression == reference
            && IsDisposeName(access.Name)
        )
            return access.Parent is InvocationExpressionSyntax invocation
                && invocation.Expression == access
                && ResolvesToDispose(invocation, context);

        // `_cts?.Dispose()` — the call hangs off the conditional access, not off the field.
        return reference.Parent is ConditionalAccessExpressionSyntax conditional
            && conditional.Expression == reference
            && conditional.WhenNotNull
                is InvocationExpressionSyntax
                {
                    Expression: MemberBindingExpressionSyntax binding
                } conditionalCall
            && IsDisposeName(binding.Name)
            && ResolvesToDispose(conditionalCall, context);
    }

    private static bool IsDisposeName(SimpleNameSyntax name) =>
        name.Identifier.Text is "Dispose" or "DisposeAsync";

    /// <summary>
    /// Returns <c>true</c> when the call really is the framework disposal, rather than something
    /// merely spelled that way.
    /// </summary>
    /// <remarks>
    /// The name alone is not enough. <c>CancellationTokenSource</c> has no instance
    /// <c>DisposeAsync</c>, so every <c>_cts.DisposeAsync()</c> is an extension method that may do
    /// anything at all — and an extension called <c>Dispose</c> is equally free not to dispose.
    /// CC014 makes the same distinction for locals.
    /// </remarks>
    private static bool ResolvesToDispose(
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context
    ) =>
        context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is IMethodSymbol { Name: "Dispose", Parameters.Length: 0, IsExtensionMethod: false } target
        && (
            // Exactly the framework type, not a subclass: CancellationTokenSource.Dispose() is not
            // virtual, so a derived `new void Dispose() { }` hides it without disposing anything.
            IsExactlyCancellationTokenSource(target.ContainingType)
            || target.ContainingType?.SpecialType == SpecialType.System_IDisposable
        );

    private static bool IsExactlyCancellationTokenSource(ITypeSymbol? type) =>
        type is { ContainingType: null, Name: "CancellationTokenSource" }
        && type.ContainingNamespace?.ToDisplayString() == "System.Threading";

    /// <summary>
    /// Walks outward past parentheses and null-forgiving operators, which are compile-time only and
    /// do not change what the expression denotes. Mirrors CC014.
    /// </summary>
    private static ExpressionSyntax UnwrapCompileTimeWrappers(ExpressionSyntax expression)
    {
        var value = expression;
        while (true)
        {
            switch (value.Parent)
            {
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression)
                        && postfix.Operand == value:
                    value = postfix;
                    continue;
                case ParenthesizedExpressionSyntax parenthesized
                    when parenthesized.Expression == value:
                    value = parenthesized;
                    continue;
                // A cast changes the static type, not the object — `((IDisposable)_cts).Dispose()`
                // disposes the same source.
                case CastExpressionSyntax cast when cast.Expression == value:
                    value = cast;
                    continue;
                default:
                    return value;
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> is, or derives from,
    /// <c>System.Threading.CancellationTokenSource</c>.
    /// </summary>
    /// <remarks>
    /// The type is not sealed, and a subclass owns the same timer and registration list, so it is
    /// the same leak. Nested lookalikes are excluded — the framework type is top level.
    /// </remarks>
    private static bool IsCancellationTokenSource(ITypeSymbol? type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (
                current is { ContainingType: null, Name: "CancellationTokenSource" }
                && current.ContainingNamespace?.ToDisplayString() == "System.Threading"
            )
                return true;
        }

        return false;
    }
}
