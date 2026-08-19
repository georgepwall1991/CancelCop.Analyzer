using System.Collections.Generic;
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
/// The fixer rewrites a safe accept to <c>await Accept*Async</c>, flowing an
/// in-scope token when the rewritten call still binds.
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

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    /// <summary>
    /// Property key set when the diagnostic is correct but no safe rewrite exists.
    /// </summary>
    public const string NoFixProperty = "NoFix";

    /// <summary>
    /// Property key for the TAP token parameter name when the original call
    /// already uses named arguments.
    /// </summary>
    public const string TokenArgumentNameProperty = "TokenArgumentName";

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

        var properties = ImmutableDictionary<string, string?>.Empty;

        if (CancellationTokenHelpers.AwaitInsertionIsUnsafe(context.SemanticModel, invocation))
            properties = properties.Add(NoFixProperty, "await-unsafe");

        if (
            !properties.ContainsKey(NoFixProperty)
            && IsInsideAcceptAsync(context, invocation, listenerType, definition)
        )
            properties = properties.Add(NoFixProperty, "self-async");

        var tokenName = CancellationTokenHelpers
            .FindEnclosingCancellationToken(invocation, context.SemanticModel)
            ?.ExpressionText;

        var tokenArgumentName =
            tokenName != null && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
                ? FindTokenParameterName(
                    ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
                    definition,
                    context
                )
                : null;

        if (
            tokenName != null
            && !ResolvesToUsableCounterpart(
                context,
                invocation,
                definition,
                tokenName,
                tokenArgumentName
            )
        )
        {
            tokenName = null;
            tokenArgumentName = null;
        }

        if (
            ResolvesToUsableCounterpart(
                context,
                invocation,
                definition,
                tokenName,
                tokenArgumentName
            )
        )
        {
            if (tokenName != null)
                properties = properties.Add(TokenNameProperty, tokenName);

            if (tokenArgumentName != null)
                properties = properties.Add(TokenArgumentNameProperty, tokenArgumentName);

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
            );
            return;
        }

        if (
            !ReachesCounterpart(
                ReceiverTypeOf(context, invocation) ?? method.ReceiverType,
                definition,
                context
            )
        )
            return;

        if (!properties.ContainsKey(NoFixProperty))
            properties = properties.Add(NoFixProperty, "no-safe-rewrite");

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, definition.Name)
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

    private static bool IsInsideAcceptAsync(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol listenerType,
        IMethodSymbol accept
    )
    {
        var enclosing =
            context.SemanticModel.GetEnclosingSymbol(
                invocation.SpanStart,
                context.CancellationToken
            ) as IMethodSymbol;

        while (
            enclosing is { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction }
        )
            enclosing = enclosing.ContainingSymbol as IMethodSymbol;

        if (
            enclosing is null
            || enclosing.Name != accept.Name + "Async"
            || !IsTcpListenerOrDerived(enclosing.ContainingType, listenerType)
            || !IsUsableAsyncCounterpart(enclosing, accept)
            || !MatchesAcceptShape(enclosing, accept)
        )
            return false;

        return invocation.Expression switch
        {
            IdentifierNameSyntax => true,
            MemberAccessExpressionSyntax member => ReceiverMayAliasThis(context, member.Expression),
            _ => false,
        };
    }

    private static bool ReceiverMayAliasThis(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax receiver
    ) =>
        ExpressionMayAliasThis(
            context,
            receiver,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default)
        );

    private static bool ExpressionMayAliasThis(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression,
        HashSet<ISymbol> seen
    )
    {
        expression = UnwrapIdentity(expression);

        switch (expression)
        {
            case ThisExpressionSyntax:
            case BaseExpressionSyntax:
                return true;
            case ConditionalExpressionSyntax conditional:
                return ExpressionMayAliasThis(context, conditional.WhenTrue, seen)
                    || ExpressionMayAliasThis(context, conditional.WhenFalse, seen);
            case MemberAccessExpressionSyntax member
                when UnwrapIdentity(member.Expression)
                    is ThisExpressionSyntax
                        or BaseExpressionSyntax:
                expression = member.Name;
                break;
        }

        if (expression is not SimpleNameSyntax)
            return false;

        var symbol = context
            .SemanticModel.GetSymbolInfo(expression, context.CancellationToken)
            .Symbol;

        if (
            symbol is IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false }
            && IsInstanceMemberOfEnclosingType(context, symbol, expression)
        )
            return true;

        if (symbol is not (ILocalSymbol or IParameterSymbol))
            return false;

        return SymbolIsAssignedFromThis(context, symbol, expression, seen);
    }

    private static bool IsInstanceMemberOfEnclosingType(
        SyntaxNodeAnalysisContext context,
        ISymbol symbol,
        SyntaxNode location
    )
    {
        var enclosing =
            context.ContainingSymbol as INamedTypeSymbol
            ?? (context.ContainingSymbol as IMethodSymbol)?.ContainingType
            ?? context
                .SemanticModel.GetEnclosingSymbol(location.SpanStart, context.CancellationToken)
                ?.ContainingType;

        if (enclosing is null)
            return false;

        for (var current = enclosing; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, symbol.ContainingType))
                return true;
        }

        return false;
    }

    private static bool SymbolIsAssignedFromThis(
        SyntaxNodeAnalysisContext context,
        ISymbol symbol,
        SyntaxNode location,
        HashSet<ISymbol> seen
    )
    {
        if (!seen.Add(symbol))
            return false;

        var body = EnclosingFunctionBody(location);
        if (body is null)
            return false;

        foreach (var declarator in body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (
                !SymbolEqualityComparer.Default.Equals(
                    context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken),
                    symbol
                )
            )
                continue;

            if (
                declarator.Initializer?.Value is { } init
                && ExpressionMayAliasThis(context, init, seen)
            )
                return true;
        }

        foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (!IsAssignmentTo(context, assignment.Left, symbol))
                continue;

            if (ExpressionMayAliasThis(context, assignment.Right, seen))
                return true;
        }

        return false;
    }

    private static bool IsAssignmentTo(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax left,
        ISymbol symbol
    )
    {
        var assigned = context
            .SemanticModel.GetSymbolInfo(UnwrapIdentity(left), context.CancellationToken)
            .Symbol;
        return SymbolEqualityComparer.Default.Equals(assigned, symbol);
    }

    private static ExpressionSyntax UnwrapIdentity(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax paren:
                    expression = paren.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SuppressNullableWarningExpression
                } bang:
                    expression = bang.Operand;
                    continue;
                case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression } asExpr:
                    expression = asExpr.Left;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static SyntaxNode? EnclosingFunctionBody(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax { Body: { } body }:
                    return body;
                case MethodDeclarationSyntax { ExpressionBody: { } expr }:
                    return expr;
                case ConstructorDeclarationSyntax { Body: { } body }:
                    return body;
                case ConstructorDeclarationSyntax { ExpressionBody: { } expr }:
                    return expr;
                case AccessorDeclarationSyntax { Body: { } body }:
                    return body;
            }
        }

        return null;
    }

    private static bool IsTcpListenerOrDerived(ITypeSymbol? type, INamedTypeSymbol listenerType)
    {
        while (type != null)
        {
            if (SymbolEqualityComparer.Default.Equals(type, listenerType))
                return true;
            type = type.BaseType;
        }

        return false;
    }

    private static ITypeSymbol? ReceiverTypeOf(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation
    )
    {
        var receiver = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            MemberBindingExpressionSyntax => invocation
                .Ancestors()
                .OfType<ConditionalAccessExpressionSyntax>()
                .FirstOrDefault()
                ?.Expression,
            _ => null,
        };

        return receiver is null
            ? null
            : context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type;
    }

    private static string? FindTokenParameterName(
        ITypeSymbol? receiverType,
        IMethodSymbol accept,
        SyntaxNodeAnalysisContext context
    )
    {
        foreach (var member in ReachableAcceptAsync(receiverType, accept, context))
        {
            if (!MatchesAcceptShape(member, accept))
                continue;

            if (member.Parameters.IsEmpty)
                continue;

            var last = member.Parameters[member.Parameters.Length - 1];
            if (CancellationTokenHelpers.IsCancellationToken(last.Type))
                return last.Name;
        }

        return "cancellationToken";
    }

    private static bool ResolvesToUsableCounterpart(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol accept,
        string? tokenName,
        string? tokenArgumentName
    )
    {
        var speculative = CancellationTokenHelpers.BuildRenamedInvocation(
            invocation,
            accept.Name + "Async",
            tokenName,
            tokenArgumentName
        );
        if (speculative is null)
            return false;

        var bound =
            context
                .SemanticModel.GetSpeculativeSymbolInfo(
                    invocation.SpanStart,
                    speculative,
                    SpeculativeBindingOption.BindAsExpression
                )
                .Symbol as IMethodSymbol;
        return bound is not null
            && IsUsableAsyncCounterpart(bound, accept)
            && MatchesAcceptShape(bound, accept);
    }

    private static bool ReachesCounterpart(
        ITypeSymbol? receiverType,
        IMethodSymbol accept,
        SyntaxNodeAnalysisContext context
    )
    {
        foreach (var member in ReachableAcceptAsync(receiverType, accept, context))
        {
            if (IsUsableAsyncCounterpart(member, accept) && MatchesAcceptShape(member, accept))
                return true;
        }

        return false;
    }

    private static IEnumerable<IMethodSymbol> ReachableAcceptAsync(
        ITypeSymbol? receiverType,
        IMethodSymbol accept,
        SyntaxNodeAnalysisContext context
    )
    {
        var enclosing =
            context.ContainingSymbol
            ?? context.SemanticModel.GetEnclosingSymbol(
                context.Node.SpanStart,
                context.CancellationToken
            );
        var compilation = context.SemanticModel.Compilation;
        ISymbol within =
            enclosing as INamedTypeSymbol
            ?? enclosing?.ContainingType
            ?? (ISymbol)compilation.Assembly;
        var seen = new List<IMethodSymbol>();
        var asyncName = accept.Name + "Async";

        for (var current = receiverType; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(asyncName).OfType<IMethodSymbol>())
            {
                if (within is not null && !compilation.IsSymbolAccessibleWithin(member, within))
                    continue;

                if (seen.Any(s => SameSignature(s, member)))
                    continue;

                seen.Add(member);
                yield return member;
            }
        }
    }

    private static bool IsUsableAsyncCounterpart(IMethodSymbol? bound, IMethodSymbol accept)
    {
        if (bound is not { IsStatic: false })
            return false;

        if (bound.Name != accept.Name + "Async")
            return false;

        if (!IsTaskOf(bound.ReturnType, accept.ReturnType))
            return false;

        if (bound.Parameters.IsEmpty)
            return true;

        var last = bound.Parameters[bound.Parameters.Length - 1];
        return CancellationTokenHelpers.IsCancellationToken(last.Type);
    }

    private static bool MatchesAcceptShape(IMethodSymbol tap, IMethodSymbol accept)
    {
        var tapArgs = tap
            .Parameters.Where(p => !CancellationTokenHelpers.IsCancellationToken(p.Type))
            .ToArray();
        if (tapArgs.Length != accept.Parameters.Length)
            return false;

        for (var i = 0; i < tapArgs.Length; i++)
        {
            if (tapArgs[i].RefKind != accept.Parameters[i].RefKind)
                return false;

            if (!SymbolEqualityComparer.Default.Equals(tapArgs[i].Type, accept.Parameters[i].Type))
                return false;
        }

        return true;
    }

    private static bool SameSignature(IMethodSymbol left, IMethodSymbol right)
    {
        if (left.Parameters.Length != right.Parameters.Length)
            return false;

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (left.Parameters[i].RefKind != right.Parameters[i].RefKind)
                return false;

            if (
                !SymbolEqualityComparer.Default.Equals(
                    left.Parameters[i].Type,
                    right.Parameters[i].Type
                )
            )
                return false;
        }

        return true;
    }

    private static bool IsTaskOf(ITypeSymbol type, ITypeSymbol expected)
    {
        for (
            var current = type as INamedTypeSymbol;
            current is not null;
            current = current.BaseType
        )
        {
            var definition = current.OriginalDefinition;
            if (definition.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks")
                continue;

            if (definition.Name is not ("Task" or "ValueTask") || current.TypeArguments.Length != 1)
                continue;

            return SymbolEqualityComparer.Default.Equals(current.TypeArguments[0], expected);
        }

        return false;
    }
}
