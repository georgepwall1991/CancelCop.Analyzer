using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>System.Net.Sockets.UdpClient.Receive</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC039
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>UdpClient.Receive</c> parks a thread-pool thread until a datagram arrives. That wait is
/// unbounded and is not a <c>CancellationToken</c>. <c>ReceiveAsync</c> yields the thread
/// and accepts a token on modern .NET.
/// </para>
/// <para>
/// <b>Why this is not CC036/CC037/CC038:</b> CC036 is symbol-gated to <c>Socket</c>;
/// CC037 is <c>TcpClient.Connect</c>; CC038 is <c>TcpListener</c> accept. The UDP wrapper
/// is a fourth type — verified empirically against the shipped analyzers.
/// </para>
/// <para>
/// Analyzer-only in this slice: the rewrite is signature-compatible, but a fixer is a
/// follow-up. Report first, rewrite later.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
/// {
///     IPEndPoint? remote = null;
///     client.Receive(ref remote);   // CC039
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingUdpClientAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC039";

    private static readonly LocalizableString Title =
        "Avoid blocking UdpClient.Receive in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'UdpClient.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "UdpClient.Receive parks a thread-pool thread until a datagram arrives; in async code use ReceiveAsync. A CancellationToken overload exists on modern .NET; older targets have the tokenless form only.";
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
            var udpType = start.Compilation.GetTypeByMetadataName("System.Net.Sockets.UdpClient");
            if (udpType is null)
                return;

            var socketType = start.Compilation.GetTypeByMetadataName("System.Net.Sockets.Socket");

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, udpType, socketType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol udpType,
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
        if (invokedName is null || invokedName.Identifier.Text != "Receive")
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
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, udpType)
            || definition.Name != "Receive"
        )
            return;

        if (udpType.GetMembers("ReceiveAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        if (AvailableWasPositiveOnThisClient(invocation, context, udpType))
            return;

        if (socketType != null && ClientNonBlockingIsSet(invocation, context, udpType, socketType))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), definition.Name)
        );
    }

    /// <summary>
    /// True when the receive sits in a branch that requires <c>Available</c> to
    /// be greater than zero — a datagram is already queued. <c>Available == 0</c>
    /// is the blocking path and does not exempt. An inverted early-exit
    /// (<c>if (Available == 0) continue;</c> then receive) is the positive path.
    /// </summary>
    private static bool AvailableWasPositiveOnThisClient(
        InvocationExpressionSyntax receive,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol udpType
    )
    {
        var receiveReceiver = GetSimpleReceiverSymbol(receive, context);
        var receiveIsCurrent = IsCurrentInstanceCall(receive);
        if (receiveReceiver is null && !receiveIsCurrent)
            return false;

        var scope = GetScope(receive);
        if (scope is null)
            return false;

        var available = udpType.GetMembers("Available").OfType<IPropertySymbol>().FirstOrDefault();
        if (available is null)
            return false;

        foreach (var ancestor in receive.Ancestors())
        {
            if (ancestor == scope)
                break;

            ExpressionSyntax? availableAccess = null;
            var polarity = AvailablePolarity.Unknown;
            SyntaxNode? guardedBody = null;

            switch (ancestor)
            {
                case IfStatementSyntax ifStatement:
                    if (
                        !TryGetAvailablePolarity(
                            ifStatement.Condition,
                            receive,
                            receiveReceiver,
                            receiveIsCurrent,
                            available,
                            context,
                            out polarity,
                            out availableAccess
                        )
                    )
                        continue;
                    if (
                        ifStatement.Statement.Span.Contains(receive.Span)
                        && polarity == AvailablePolarity.ThenPositive
                    )
                        guardedBody = ifStatement.Statement;
                    else if (
                        ifStatement.Else?.Statement.Span.Contains(receive.Span) == true
                        && polarity == AvailablePolarity.ElsePositive
                    )
                        guardedBody = ifStatement.Else.Statement;
                    break;
                case WhileStatementSyntax whileStatement:
                    if (
                        !TryGetAvailablePolarity(
                            whileStatement.Condition,
                            receive,
                            receiveReceiver,
                            receiveIsCurrent,
                            available,
                            context,
                            out polarity,
                            out availableAccess
                        )
                    )
                        continue;
                    if (
                        polarity == AvailablePolarity.ThenPositive
                        && whileStatement.Statement.Span.Contains(receive.Span)
                    )
                        guardedBody = whileStatement.Statement;
                    break;
                case BlockSyntax block:
                    if (
                        !EarlyExitAvailableDominates(
                            block,
                            receive,
                            receiveReceiver,
                            receiveIsCurrent,
                            available,
                            context,
                            out availableAccess
                        )
                    )
                        continue;
                    guardedBody = block;
                    break;
            }

            if (
                guardedBody != null
                && availableAccess != null
                && !ReceiverWasReassignedAfter(
                    scope,
                    availableAccess,
                    receive,
                    receiveReceiver,
                    context
                )
            )
                return true;
        }

        return false;
    }

    private static bool EarlyExitAvailableDominates(
        BlockSyntax block,
        InvocationExpressionSyntax receive,
        ISymbol? receiveReceiver,
        bool receiveIsCurrent,
        IPropertySymbol available,
        SyntaxNodeAnalysisContext context,
        out ExpressionSyntax? availableAccess
    )
    {
        availableAccess = null;
        var receiveStatement = block.Statements.FirstOrDefault(statement =>
            statement.Span.Contains(receive.Span)
        );
        if (receiveStatement is null)
            return false;

        foreach (var preceding in block.Statements)
        {
            if (preceding.SpanStart >= receiveStatement.SpanStart)
                break;
            if (preceding is not IfStatementSyntax ifStatement)
                continue;
            if (
                !TryGetAvailablePolarity(
                    ifStatement.Condition,
                    receive,
                    receiveReceiver,
                    receiveIsCurrent,
                    available,
                    context,
                    out var polarity,
                    out var access
                )
            )
                continue;
            // Then-body is the empty-queue path; remaining statements run only
            // when a datagram is queued.
            if (
                polarity != AvailablePolarity.ElsePositive
                || !AllPathsExit(ifStatement.Statement)
                || access is null
            )
                continue;

            availableAccess = access;
            return true;
        }

        return false;
    }

    private enum AvailablePolarity
    {
        Unknown,
        ThenPositive,
        ElsePositive,
    }

    /// <summary>
    /// Returns true when <paramref name="condition"/> compares this client's
    /// <c>Available</c> to a constant. <see cref="AvailablePolarity.ThenPositive"/>
    /// means the then/while body only runs with a queued datagram.
    /// <see cref="AvailablePolarity.ElsePositive"/> means the else path (or the
    /// remainder after an empty-queue early-exit) is the queued path.
    /// </summary>
    private static bool TryGetAvailablePolarity(
        ExpressionSyntax condition,
        InvocationExpressionSyntax receive,
        ISymbol? receiveReceiver,
        bool receiveIsCurrent,
        IPropertySymbol available,
        SyntaxNodeAnalysisContext context,
        out AvailablePolarity polarity,
        out ExpressionSyntax? availableAccess
    )
    {
        polarity = AvailablePolarity.Unknown;
        availableAccess = null;
        condition = Unwrap(condition);

        if (
            condition is BinaryExpressionSyntax andCondition
            && andCondition.IsKind(SyntaxKind.LogicalAndExpression)
        )
        {
            foreach (var operand in AndOperands(andCondition))
            {
                if (
                    TryGetAvailablePolarity(
                        operand,
                        receive,
                        receiveReceiver,
                        receiveIsCurrent,
                        available,
                        context,
                        out var operandPolarity,
                        out var operandAccess
                    )
                    && operandPolarity == AvailablePolarity.ThenPositive
                )
                {
                    polarity = AvailablePolarity.ThenPositive;
                    availableAccess = operandAccess;
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
            condition is not BinaryExpressionSyntax binary
            || (
                !binary.IsKind(SyntaxKind.GreaterThanExpression)
                && !binary.IsKind(SyntaxKind.GreaterThanOrEqualExpression)
                && !binary.IsKind(SyntaxKind.LessThanExpression)
                && !binary.IsKind(SyntaxKind.LessThanOrEqualExpression)
                && !binary.IsKind(SyntaxKind.EqualsExpression)
                && !binary.IsKind(SyntaxKind.NotEqualsExpression)
            )
        )
            return false;

        var left = Unwrap(binary.Left);
        var right = Unwrap(binary.Right);
        var leftIsAvailable = IsThisClientsAvailable(
            left,
            receive,
            receiveReceiver,
            receiveIsCurrent,
            available,
            context
        );
        var rightIsAvailable = IsThisClientsAvailable(
            right,
            receive,
            receiveReceiver,
            receiveIsCurrent,
            available,
            context
        );
        if (leftIsAvailable == rightIsAvailable)
            return false;

        var constantExpr = leftIsAvailable ? right : left;
        var constant = context.SemanticModel.GetConstantValue(
            constantExpr,
            context.CancellationToken
        );
        if (!constant.HasValue || constant.Value is not int threshold)
            return false;

        availableAccess = leftIsAvailable ? left : right;
        var kind = binary.Kind();
        if (!leftIsAvailable)
            kind = FlipComparison(kind);

        polarity = PolarityForComparison(kind, threshold);
        if (negated)
            polarity = FlipPolarity(polarity);
        return polarity != AvailablePolarity.Unknown;
    }

    private static AvailablePolarity PolarityForComparison(SyntaxKind kind, int threshold) =>
        kind switch
        {
            // Available > t is a queued datagram when t >= 0. Available > -1 is
            // always true and proves nothing.
            SyntaxKind.GreaterThanExpression => threshold >= 0
                ? AvailablePolarity.ThenPositive
                : AvailablePolarity.Unknown,
            SyntaxKind.GreaterThanOrEqualExpression => threshold >= 1
                ? AvailablePolarity.ThenPositive
                : AvailablePolarity.Unknown,
            SyntaxKind.EqualsExpression => threshold > 0 ? AvailablePolarity.ThenPositive
            : threshold == 0 ? AvailablePolarity.ElsePositive
            : AvailablePolarity.Unknown,
            SyntaxKind.NotEqualsExpression => threshold == 0 ? AvailablePolarity.ThenPositive
            : threshold > 0 ? AvailablePolarity.ElsePositive
            : AvailablePolarity.Unknown,
            SyntaxKind.LessThanExpression => threshold >= 1
                ? AvailablePolarity.ElsePositive
                : AvailablePolarity.Unknown,
            SyntaxKind.LessThanOrEqualExpression => threshold >= 0
                ? AvailablePolarity.ElsePositive
                : AvailablePolarity.Unknown,
            _ => AvailablePolarity.Unknown,
        };

    private static AvailablePolarity FlipPolarity(AvailablePolarity polarity) =>
        polarity switch
        {
            AvailablePolarity.ThenPositive => AvailablePolarity.ElsePositive,
            AvailablePolarity.ElsePositive => AvailablePolarity.ThenPositive,
            _ => AvailablePolarity.Unknown,
        };

    private static SyntaxKind FlipComparison(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.GreaterThanExpression => SyntaxKind.LessThanExpression,
            SyntaxKind.GreaterThanOrEqualExpression => SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.LessThanExpression => SyntaxKind.GreaterThanExpression,
            SyntaxKind.LessThanOrEqualExpression => SyntaxKind.GreaterThanOrEqualExpression,
            _ => kind,
        };

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

    private static bool IsThisClientsAvailable(
        ExpressionSyntax expression,
        InvocationExpressionSyntax receive,
        ISymbol? receiveReceiver,
        bool receiveIsCurrent,
        IPropertySymbol available,
        SyntaxNodeAnalysisContext context
    )
    {
        expression = Unwrap(expression);
        if (!IsAvailableProperty(expression, available, context))
            return false;

        if (expression is IdentifierNameSyntax)
            return receiveIsCurrent;

        if (expression is not MemberAccessExpressionSyntax member)
            return false;

        var owner = Unwrap(member.Expression);
        if (receiveIsCurrent)
            return owner is ThisExpressionSyntax or BaseExpressionSyntax;

        return SymbolEqualityComparer.Default.Equals(GetSymbol(context, owner), receiveReceiver);
    }

    private static bool IsAvailableProperty(
        ExpressionSyntax expression,
        IPropertySymbol available,
        SyntaxNodeAnalysisContext context
    )
    {
        var symbol = GetSymbol(context, expression);
        if (SymbolEqualityComparer.Default.Equals(symbol, available))
            return true;

        if (
            expression is MemberAccessExpressionSyntax member
            && SymbolEqualityComparer.Default.Equals(GetSymbol(context, member.Name), available)
        )
            return true;

        return false;
    }

    private static bool ClientNonBlockingIsSet(
        InvocationExpressionSyntax receive,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol udpType,
        INamedTypeSymbol socketType
    )
    {
        var receiveReceiver = GetSimpleReceiverSymbol(receive, context);
        var receiveIsCurrent = IsCurrentInstanceCall(receive);
        if (receiveReceiver is null && !receiveIsCurrent)
            return false;

        var blockingProperty = socketType
            .GetMembers("Blocking")
            .OfType<IPropertySymbol>()
            .FirstOrDefault();
        var clientProperty = udpType
            .GetMembers("Client")
            .OfType<IPropertySymbol>()
            .FirstOrDefault();
        if (blockingProperty is null || clientProperty is null)
            return false;

        var scope = GetScope(receive);
        if (scope is null)
            return false;

        var effective = scope
            .DescendantNodes(descendIntoChildren: child =>
                child == scope
                || child is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
            )
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment =>
                assignment.SpanStart < receive.SpanStart
                && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && SymbolEqualityComparer.Default.Equals(
                    context
                        .SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken)
                        .Symbol,
                    blockingProperty
                )
                && AssignmentIsThisClientsBlocking(
                    assignment,
                    context,
                    clientProperty,
                    receiveReceiver,
                    receiveIsCurrent
                )
            )
            .OrderBy(assignment => assignment.SpanStart)
            .LastOrDefault();

        return effective != null
            && context.SemanticModel.GetConstantValue(effective.Right, context.CancellationToken)
                is { HasValue: true, Value: false }
            && !ReceiverWasReassignedAfter(scope, effective, receive, receiveReceiver, context);
    }

    private static bool AssignmentIsThisClientsBlocking(
        AssignmentExpressionSyntax assignment,
        SyntaxNodeAnalysisContext context,
        IPropertySymbol clientProperty,
        ISymbol? receiveReceiver,
        bool receiveIsCurrent
    )
    {
        if (Unwrap(assignment.Left) is not MemberAccessExpressionSyntax blockingAccess)
            return false;

        var clientExpr = Unwrap(blockingAccess.Expression);
        if (!SymbolEqualityComparer.Default.Equals(GetSymbol(context, clientExpr), clientProperty))
            return false;

        if (receiveIsCurrent)
            return clientExpr is IdentifierNameSyntax
                || (
                    clientExpr is MemberAccessExpressionSyntax clientAccess
                    && Unwrap(clientAccess.Expression)
                        is ThisExpressionSyntax
                            or BaseExpressionSyntax
                );

        return clientExpr is MemberAccessExpressionSyntax ownerAccess
            && SymbolEqualityComparer.Default.Equals(
                GetSymbol(context, Unwrap(ownerAccess.Expression)),
                receiveReceiver
            );
    }

    private static bool ReceiverWasReassignedAfter(
        SyntaxNode scope,
        SyntaxNode guard,
        InvocationExpressionSyntax receive,
        ISymbol? receiveReceiver,
        SyntaxNodeAnalysisContext context
    )
    {
        if (receiveReceiver is null)
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
                    && assignment.SpanStart < receive.SpanStart
                    && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    && WritesReceiver(assignment, receiveReceiver, context)
                )
        )
            return true;

        return nodes
            .OfType<ArgumentSyntax>()
            .Any(argument =>
                argument.SpanStart > guard.SpanStart
                && argument.SpanStart < receive.SpanStart
                && (
                    argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                    || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
                )
                && SymbolEqualityComparer.Default.Equals(
                    GetSymbol(context, Unwrap(argument.Expression)),
                    receiveReceiver
                )
            );
    }

    private static bool WritesReceiver(
        AssignmentExpressionSyntax assignment,
        ISymbol receiveReceiver,
        SyntaxNodeAnalysisContext context
    )
    {
        var left = Unwrap(assignment.Left);
        if (SymbolEqualityComparer.Default.Equals(GetSymbol(context, left), receiveReceiver))
            return true;

        if (left is not TupleExpressionSyntax tuple)
            return false;

        return tuple.Arguments.Any(argument =>
            SymbolEqualityComparer.Default.Equals(
                GetSymbol(context, Unwrap(argument.Expression)),
                receiveReceiver
            ) || WritesReceiverFromExpression(argument.Expression, receiveReceiver, context)
        );
    }

    private static bool WritesReceiverFromExpression(
        ExpressionSyntax expression,
        ISymbol receiveReceiver,
        SyntaxNodeAnalysisContext context
    )
    {
        expression = Unwrap(expression);
        if (expression is not TupleExpressionSyntax tuple)
            return false;

        return tuple.Arguments.Any(argument =>
            SymbolEqualityComparer.Default.Equals(
                GetSymbol(context, Unwrap(argument.Expression)),
                receiveReceiver
            ) || WritesReceiverFromExpression(argument.Expression, receiveReceiver, context)
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
