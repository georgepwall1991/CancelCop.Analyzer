using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>System.Net.Sockets.TcpListener</c> accept inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC038
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>AcceptTcpClient</c> / <c>AcceptSocket</c> park a thread-pool thread until a client
/// connects. That wait is unbounded and is not a <c>CancellationToken</c>. The
/// <c>Accept*Async</c> counterparts yield the thread and accept a token.
/// </para>
/// <para>
/// <b>Why this is not CC036/CC037:</b> CC036 is symbol-gated to <c>Socket</c>; CC037 is
/// <c>TcpClient.Connect</c>. The listener accept path is a third type — verified
/// empirically against the shipped analyzers.
/// </para>
/// <para>
/// Analyzer-only in this slice: the rewrite is signature-compatible, but a fixer is a
/// follow-up. Report first, rewrite later.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
/// {
///     listener.AcceptTcpClient();   // CC038
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingTcpListenerAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC038";

    private static readonly ImmutableHashSet<string> BlockingMembers = ImmutableHashSet.Create(
        "AcceptTcpClient",
        "AcceptSocket"
    );

    private static readonly LocalizableString Title =
        "Avoid blocking TcpListener accept in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'TcpListener.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "TcpListener.AcceptTcpClient and AcceptSocket park a thread-pool thread until a client connects; in async code use AcceptTcpClientAsync / AcceptSocketAsync. A CancellationToken overload exists on modern .NET; older targets have the tokenless form only.";
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

        context.RegisterCompilationStartAction(start =>
        {
            var listenerType = start.Compilation.GetTypeByMetadataName(
                "System.Net.Sockets.TcpListener"
            );
            if (listenerType is null)
                return;

            var socketType = start.Compilation.GetTypeByMetadataName("System.Net.Sockets.Socket");

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, listenerType, socketType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol listenerType,
        INamedTypeSymbol? socketType
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var invokedName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            IdentifierNameSyntax identifier => identifier,
            _ => null,
        };
        if (invokedName is null || !BlockingMembers.Contains(invokedName.Identifier.Text))
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        var definition = method;
        while (definition.OverriddenMethod != null)
            definition = definition.OverriddenMethod;

        if (
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, listenerType)
            || !BlockingMembers.Contains(definition.Name)
        )
            return;

        if (listenerType.GetMembers(definition.Name + "Async").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        if (PendingWasCheckedOnThisListener(invocation, context, listenerType))
            return;

        if (
            socketType != null
            && ServerNonBlockingIsSet(invocation, context, listenerType, socketType)
        )
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), definition.Name)
        );
    }

    /// <summary>
    /// True when the accept sits in a branch that requires <c>Pending()</c> to be
    /// true — the documented non-blocking path. A negated guard
    /// (<c>if (!listener.Pending()) Accept</c>) is the blocking path and does
    /// not exempt. An inverted early-exit
    /// (<c>if (!Pending()) continue;</c> then accept) and a conjunct
    /// (<c>while (flag &amp;&amp; Pending())</c>) are the positive path.
    /// </summary>
    private static bool PendingWasCheckedOnThisListener(
        InvocationExpressionSyntax accept,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol listenerType
    )
    {
        var acceptReceiver = GetSimpleReceiverSymbol(accept, context);
        var acceptIsCurrent = IsCurrentInstanceCall(accept);
        if (acceptReceiver is null && !acceptIsCurrent)
            return false;

        var scope = GetScope(accept);
        if (scope is null)
            return false;

        var pending = listenerType
            .GetMembers("Pending")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.Parameters.IsEmpty);
        if (pending is null)
            return false;

        foreach (var ancestor in accept.Ancestors())
        {
            if (ancestor == scope)
                break;

            InvocationExpressionSyntax? pendingCall = null;
            var pendingMustBeTrue = false;
            SyntaxNode? guardedBody = null;

            switch (ancestor)
            {
                case IfStatementSyntax ifStatement:
                    if (
                        !TryGetPendingPolarity(
                            ifStatement.Condition,
                            accept,
                            acceptReceiver,
                            acceptIsCurrent,
                            pending,
                            context,
                            out pendingMustBeTrue,
                            out pendingCall
                        )
                    )
                        continue;
                    if (ifStatement.Statement.Span.Contains(accept.Span) && pendingMustBeTrue)
                        guardedBody = ifStatement.Statement;
                    else if (
                        ifStatement.Else?.Statement.Span.Contains(accept.Span) == true
                        && !pendingMustBeTrue
                    )
                        guardedBody = ifStatement.Else.Statement;
                    break;
                case WhileStatementSyntax whileStatement:
                    if (
                        !TryGetPendingPolarity(
                            whileStatement.Condition,
                            accept,
                            acceptReceiver,
                            acceptIsCurrent,
                            pending,
                            context,
                            out pendingMustBeTrue,
                            out pendingCall
                        )
                    )
                        continue;
                    if (pendingMustBeTrue && whileStatement.Statement.Span.Contains(accept.Span))
                        guardedBody = whileStatement.Statement;
                    break;
                case BlockSyntax block:
                    // if (!Pending()) { ... continue/return; } accept — the if is a
                    // sibling, not an ancestor, so the cases above never see it.
                    if (
                        !EarlyExitPendingDominates(
                            block,
                            accept,
                            acceptReceiver,
                            acceptIsCurrent,
                            pending,
                            context,
                            out pendingCall
                        )
                    )
                        continue;
                    guardedBody = block;
                    break;
            }

            if (
                guardedBody != null
                && pendingCall != null
                && !ReceiverWasReassignedAfter(scope, pendingCall, accept, acceptReceiver, context)
            )
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when a preceding sibling <c>if</c> runs only when <c>Pending()</c> is
    /// false and every path in that body exits. Remaining statements in the
    /// block then run only on the non-blocking path.
    /// </summary>
    private static bool EarlyExitPendingDominates(
        BlockSyntax block,
        InvocationExpressionSyntax accept,
        ISymbol? acceptReceiver,
        bool acceptIsCurrent,
        IMethodSymbol pending,
        SyntaxNodeAnalysisContext context,
        out InvocationExpressionSyntax? pendingCall
    )
    {
        pendingCall = null;
        var acceptStatement = block.Statements.FirstOrDefault(statement =>
            statement.Span.Contains(accept.Span)
        );
        if (acceptStatement is null)
            return false;

        foreach (var preceding in block.Statements)
        {
            if (preceding.SpanStart >= acceptStatement.SpanStart)
                break;
            if (preceding is not IfStatementSyntax ifStatement)
                continue;
            if (
                !TryGetPendingPolarity(
                    ifStatement.Condition,
                    accept,
                    acceptReceiver,
                    acceptIsCurrent,
                    pending,
                    context,
                    out var pendingMustBeTrue,
                    out var call
                )
            )
                continue;
            if (pendingMustBeTrue || !AllPathsExit(ifStatement.Statement) || call is null)
                continue;

            pendingCall = call;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="condition"/> is this listener's
    /// <c>Pending()</c> with optional <c>!</c> / <c>== bool</c> wrappers.
    /// <paramref name="pendingMustBeTrue"/> is the value <c>Pending()</c> must
    /// have for the then/while body to run.
    /// </summary>
    private static bool TryGetPendingPolarity(
        ExpressionSyntax condition,
        InvocationExpressionSyntax accept,
        ISymbol? acceptReceiver,
        bool acceptIsCurrent,
        IMethodSymbol pending,
        SyntaxNodeAnalysisContext context,
        out bool pendingMustBeTrue,
        out InvocationExpressionSyntax? pendingCall
    )
    {
        pendingMustBeTrue = true;
        pendingCall = null;
        condition = Unwrap(condition);

        if (
            condition is BinaryExpressionSyntax andCondition
            && andCondition.IsKind(SyntaxKind.LogicalAndExpression)
        )
        {
            foreach (var operand in AndOperands(andCondition))
            {
                if (
                    TryGetPendingPolarity(
                        operand,
                        accept,
                        acceptReceiver,
                        acceptIsCurrent,
                        pending,
                        context,
                        out var operandPendingTrue,
                        out var operandCall
                    ) && operandPendingTrue
                )
                {
                    pendingMustBeTrue = true;
                    pendingCall = operandCall;
                    return true;
                }
            }

            return false;
        }

        var negated = false;
        while (
            condition is PrefixUnaryExpressionSyntax prefix
            && prefix.IsKind(SyntaxKind.LogicalNotExpression)
        )
        {
            negated = !negated;
            condition = Unwrap(prefix.Operand);
        }

        if (
            condition is BinaryExpressionSyntax binary
            && (
                binary.IsKind(SyntaxKind.EqualsExpression)
                || binary.IsKind(SyntaxKind.NotEqualsExpression)
            )
        )
        {
            var left = Unwrap(binary.Left);
            var right = Unwrap(binary.Right);
            var constant = context.SemanticModel.GetConstantValue(
                left is InvocationExpressionSyntax ? right : left,
                context.CancellationToken
            );
            var invocation =
                left is InvocationExpressionSyntax leftInvocation ? leftInvocation
                : right is InvocationExpressionSyntax rightInvocation ? rightInvocation
                : null;
            if (invocation is null || !constant.HasValue || constant.Value is not bool required)
                return false;

            if (
                !IsThisListenersPending(
                    invocation,
                    accept,
                    acceptReceiver,
                    acceptIsCurrent,
                    pending,
                    context
                )
            )
                return false;

            pendingCall = invocation;
            var equals = binary.IsKind(SyntaxKind.EqualsExpression);
            pendingMustBeTrue = equals ? required != negated : required == negated;
            return true;
        }

        if (
            condition is IsPatternExpressionSyntax isPattern
            && isPattern.Pattern is ConstantPatternSyntax constantPattern
        )
        {
            var required = context.SemanticModel.GetConstantValue(
                constantPattern.Expression,
                context.CancellationToken
            );
            if (
                Unwrap(isPattern.Expression) is InvocationExpressionSyntax patternInvocation
                && required is { HasValue: true, Value: bool patternRequired }
                && IsThisListenersPending(
                    patternInvocation,
                    accept,
                    acceptReceiver,
                    acceptIsCurrent,
                    pending,
                    context
                )
            )
            {
                pendingCall = patternInvocation;
                pendingMustBeTrue = patternRequired != negated;
                return true;
            }

            return false;
        }

        if (condition is not InvocationExpressionSyntax pendingInvocation)
            return false;
        if (
            !IsThisListenersPending(
                pendingInvocation,
                accept,
                acceptReceiver,
                acceptIsCurrent,
                pending,
                context
            )
        )
            return false;

        pendingCall = pendingInvocation;
        pendingMustBeTrue = !negated;
        return true;
    }

    private static IEnumerable<ExpressionSyntax> AndOperands(ExpressionSyntax expression)
    {
        expression = Unwrap(expression);
        if (
            expression is BinaryExpressionSyntax binary
            && binary.IsKind(SyntaxKind.LogicalAndExpression)
        )
        {
            foreach (var operand in AndOperands(binary.Left))
                yield return operand;
            foreach (var operand in AndOperands(binary.Right))
                yield return operand;
            yield break;
        }

        yield return expression;
    }

    private static bool AllPathsExit(StatementSyntax statement)
    {
        switch (statement)
        {
            case ContinueStatementSyntax:
            case ReturnStatementSyntax:
            case ThrowStatementSyntax:
            case BreakStatementSyntax:
                return true;
            case BlockSyntax { Statements.Count: > 0 } block:
                return AllPathsExit(block.Statements[block.Statements.Count - 1]);
            case IfStatementSyntax ifStatement:
                return AllPathsExit(ifStatement.Statement)
                    && ifStatement.Else != null
                    && AllPathsExit(ifStatement.Else.Statement);
            default:
                return false;
        }
    }

    private static bool IsThisListenersPending(
        InvocationExpressionSyntax invocation,
        InvocationExpressionSyntax accept,
        ISymbol? acceptReceiver,
        bool acceptIsCurrent,
        IMethodSymbol pending,
        SyntaxNodeAnalysisContext context
    ) =>
        SymbolEqualityComparer.Default.Equals(
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol,
            pending
        ) && SameListener(invocation, accept, acceptReceiver, acceptIsCurrent, context);

    private static bool ServerNonBlockingIsSet(
        InvocationExpressionSyntax accept,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol listenerType,
        INamedTypeSymbol socketType
    )
    {
        var acceptReceiver = GetSimpleReceiverSymbol(accept, context);
        var acceptIsCurrent = IsCurrentInstanceCall(accept);
        if (acceptReceiver is null && !acceptIsCurrent)
            return false;

        var blockingProperty = socketType
            .GetMembers("Blocking")
            .OfType<IPropertySymbol>()
            .FirstOrDefault();
        var serverProperty = listenerType
            .GetMembers("Server")
            .OfType<IPropertySymbol>()
            .FirstOrDefault();
        if (blockingProperty is null || serverProperty is null)
            return false;

        var scope = GetScope(accept);
        if (scope is null)
            return false;

        var effective = scope
            .DescendantNodes(descendIntoChildren: child =>
                child == scope
                || child is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
            )
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment =>
                assignment.SpanStart < accept.SpanStart
                && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && SymbolEqualityComparer.Default.Equals(
                    context
                        .SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken)
                        .Symbol,
                    blockingProperty
                )
                && AssignmentIsThisListenersServerBlocking(
                    assignment,
                    context,
                    serverProperty,
                    acceptReceiver,
                    acceptIsCurrent
                )
            )
            .OrderBy(assignment => assignment.SpanStart)
            .LastOrDefault();

        return effective != null
            && context.SemanticModel.GetConstantValue(effective.Right, context.CancellationToken)
                is { HasValue: true, Value: false }
            && !ReceiverWasReassignedAfter(scope, effective, accept, acceptReceiver, context);
    }

    private static bool SameListener(
        InvocationExpressionSyntax left,
        InvocationExpressionSyntax right,
        ISymbol? rightReceiver,
        bool rightIsCurrent,
        SyntaxNodeAnalysisContext context
    )
    {
        if (rightIsCurrent)
            return IsCurrentInstanceCall(left);

        return SymbolEqualityComparer.Default.Equals(
            GetSimpleReceiverSymbol(left, context),
            rightReceiver
        );
    }

    private static bool AssignmentIsThisListenersServerBlocking(
        AssignmentExpressionSyntax assignment,
        SyntaxNodeAnalysisContext context,
        IPropertySymbol serverProperty,
        ISymbol? acceptReceiver,
        bool acceptIsCurrent
    )
    {
        if (Unwrap(assignment.Left) is not MemberAccessExpressionSyntax blockingAccess)
            return false;

        var serverExpr = Unwrap(blockingAccess.Expression);
        if (!SymbolEqualityComparer.Default.Equals(GetSymbol(context, serverExpr), serverProperty))
            return false;

        if (acceptIsCurrent)
            return serverExpr is IdentifierNameSyntax
                || (
                    serverExpr is MemberAccessExpressionSyntax serverAccess
                    && Unwrap(serverAccess.Expression)
                        is ThisExpressionSyntax
                            or BaseExpressionSyntax
                );

        return serverExpr is MemberAccessExpressionSyntax ownerAccess
            && SymbolEqualityComparer.Default.Equals(
                GetSymbol(context, Unwrap(ownerAccess.Expression)),
                acceptReceiver
            );
    }

    private static bool ReceiverWasReassignedAfter(
        SyntaxNode scope,
        SyntaxNode guard,
        InvocationExpressionSyntax accept,
        ISymbol? acceptReceiver,
        SyntaxNodeAnalysisContext context
    )
    {
        if (acceptReceiver is null)
            return false;

        var nodes = scope.DescendantNodes(descendIntoChildren: child =>
            child == scope
            || child is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
        );

        if (
            nodes
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment =>
                    assignment.SpanStart > guard.SpanStart
                    && assignment.SpanStart < accept.SpanStart
                    && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    && WritesReceiver(assignment, acceptReceiver, context)
                )
        )
            return true;

        return nodes
            .OfType<ArgumentSyntax>()
            .Any(argument =>
                argument.SpanStart > guard.SpanStart
                && argument.SpanStart < accept.SpanStart
                && (
                    argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                    || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
                )
                && SymbolEqualityComparer.Default.Equals(
                    GetSymbol(context, Unwrap(argument.Expression)),
                    acceptReceiver
                )
            );
    }

    private static bool WritesReceiver(
        AssignmentExpressionSyntax assignment,
        ISymbol acceptReceiver,
        SyntaxNodeAnalysisContext context
    )
    {
        var left = Unwrap(assignment.Left);
        if (SymbolEqualityComparer.Default.Equals(GetSymbol(context, left), acceptReceiver))
            return true;

        if (left is not TupleExpressionSyntax tuple)
            return false;

        return tuple.Arguments.Any(argument =>
            SymbolEqualityComparer.Default.Equals(
                GetSymbol(context, Unwrap(argument.Expression)),
                acceptReceiver
            ) || WritesReceiverFromExpression(argument.Expression, acceptReceiver, context)
        );
    }

    private static bool WritesReceiverFromExpression(
        ExpressionSyntax expression,
        ISymbol acceptReceiver,
        SyntaxNodeAnalysisContext context
    )
    {
        expression = Unwrap(expression);
        if (expression is not TupleExpressionSyntax tuple)
            return false;

        return tuple.Arguments.Any(argument =>
            SymbolEqualityComparer.Default.Equals(
                GetSymbol(context, Unwrap(argument.Expression)),
                acceptReceiver
            ) || WritesReceiverFromExpression(argument.Expression, acceptReceiver, context)
        );
    }

    private static bool IsCurrentInstanceCall(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax => true,
            MemberAccessExpressionSyntax memberAccess
                when Unwrap(memberAccess.Expression)
                    is ThisExpressionSyntax
                        or BaseExpressionSyntax => true,
            _ => false,
        };
    }

    private static SyntaxNode? GetScope(SyntaxNode node) =>
        node.Ancestors()
            .FirstOrDefault(candidate =>
                candidate
                    is BaseMethodDeclarationSyntax
                        or LocalFunctionStatementSyntax
                        or AnonymousFunctionExpressionSyntax
                        or AccessorDeclarationSyntax
            )
        ?? node.Ancestors().OfType<CompilationUnitSyntax>().FirstOrDefault();

    private static ISymbol? GetSimpleReceiverSymbol(
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context
    )
    {
        var receiver = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => Unwrap(memberAccess.Expression),
            MemberBindingExpressionSyntax
                when invocation.Parent is ConditionalAccessExpressionSyntax conditional => Unwrap(
                conditional.Expression
            ),
            _ => null,
        };
        if (receiver is null)
            return null;

        if (receiver is IdentifierNameSyntax)
            return AsSimpleReceiver(GetSymbol(context, receiver));

        if (
            receiver is MemberAccessExpressionSyntax member
            && Unwrap(member.Expression) is ThisExpressionSyntax
        )
            return AsSimpleReceiver(GetSymbol(context, member.Name));

        return null;
    }

    private static ISymbol? AsSimpleReceiver(ISymbol? symbol) =>
        symbol is ILocalSymbol or IParameterSymbol or IFieldSymbol ? symbol : null;

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static ISymbol? GetSymbol(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax? expression
    ) =>
        expression is null
            ? null
            : context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
}
