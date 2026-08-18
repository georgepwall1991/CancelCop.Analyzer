using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking <c>System.Net.Sockets.TcpClient.Connect</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC037
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>TcpClient.Connect</c> parks a thread-pool thread until the handshake finishes or TCP times
/// out. That timeout can run into tens of seconds and is not a <c>CancellationToken</c>.
/// <c>ConnectAsync</c> yields the thread and accepts a token. CC036 already covers
/// <c>Socket.Connect</c>; application code almost always uses the <c>TcpClient</c> wrapper, which
/// none of the existing rules see — verified empirically against the shipped analyzers.
/// </para>
/// <para>
/// <b>Why this is not CC036:</b> CC036 is symbol-gated to <c>Socket</c> and carries a
/// <c>Socket.Blocking</c> exemption that does not apply here. Folding <c>TcpClient</c> into it
/// would mix two types and two exemption models.
/// </para>
/// <para>
/// Analyzer-only in this slice: the rewrite to <c>await ConnectAsync(..., token)</c> is
/// signature-compatible, but a fixer is a follow-up. Report first, rewrite later.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(TcpClient client, CancellationToken cancellationToken)
/// {
///     client.Connect(host, port);   // CC037
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingTcpClientAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC037";

    private static readonly LocalizableString Title =
        "Avoid blocking TcpClient.Connect in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'TcpClient.{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "TcpClient.Connect parks a thread-pool thread until the handshake finishes; in async code use ConnectAsync. A CancellationToken overload exists on modern .NET; older targets have the tokenless form only.";
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
            var tcpClientType = start.Compilation.GetTypeByMetadataName(
                "System.Net.Sockets.TcpClient"
            );
            if (tcpClientType is null)
                return;

            var socketType = start.Compilation.GetTypeByMetadataName("System.Net.Sockets.Socket");

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, tcpClientType, socketType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol tcpClientType,
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
        if (invokedName is null || invokedName.Identifier.Text != "Connect")
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
            !SymbolEqualityComparer.Default.Equals(definition.ContainingType, tcpClientType)
            || definition.Name != "Connect"
        )
            return;

        if (tcpClientType.GetMembers("ConnectAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        // Hostname Connect still does synchronous DNS (Dns.GetHostAddresses) even when the
        // underlying socket is non-blocking. Only IP/endpoint overloads can return WouldBlock.
        if (
            socketType != null
            && !ConnectUsesHostname(definition)
            && NonBlockingModeIsSetOnThisClient(invocation, context, socketType, tcpClientType)
        )
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), definition.Name)
        );
    }

    private static bool ConnectUsesHostname(IMethodSymbol method) =>
        method.Parameters.Length >= 1
        && method.Parameters[0].Type.SpecialType == SpecialType.System_String;

    /// <summary>
    /// A TcpClient receiver: a simple expression, implicit this/base, or a
    /// declared local from <c>var x = new TcpClient { … }</c>.
    /// </summary>
    private readonly struct ReceiverRef
    {
        public ReceiverRef(
            ExpressionSyntax? expression,
            bool implicitThis,
            ISymbol? declaredSymbol = null
        )
        {
            Expression = expression is null ? null : Unwrap(expression);
            ImplicitThis = implicitThis;
            DeclaredSymbol = declaredSymbol;
        }

        public ExpressionSyntax? Expression { get; }
        public bool ImplicitThis { get; }
        public ISymbol? DeclaredSymbol { get; }
    }

    /// <summary>
    /// Returns <c>true</c> when the enclosing function assigns <c>false</c> to
    /// <c>thisClient.Client.Blocking</c>.
    /// </summary>
    /// <remarks>
    /// Same walk as CC036 (last assignment before the call, nested functions skipped,
    /// a branch-conditional assignment still exempts) but the assignment must target
    /// the invoked <c>TcpClient</c>'s <c>Client</c> socket. An unrelated
    /// <c>Socket.Blocking = false</c> does not silence this call. When that
    /// relationship cannot be established, the exemption is not applied.
    /// A later write to the same receiver (or its <c>Client</c> property)
    /// invalidates the exemption — the replacement instance is still blocking.
    /// </remarks>
    private static bool NonBlockingModeIsSetOnThisClient(
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol socketType,
        INamedTypeSymbol tcpClientType
    )
    {
        var blockingProperty = socketType
            .GetMembers("Blocking")
            .OfType<IPropertySymbol>()
            .FirstOrDefault();
        if (blockingProperty is null)
            return false;

        if (GetConnectReceiver(invocation) is not { } connectClient)
            return false;

        // Property/method receivers can return a different instance on each
        // read. Only locals, parameters, fields, and implicit/this/base are
        // stable enough for an exemption.
        if (!IsSimpleReceiver(connectClient, context))
            return false;

        var scope =
            invocation
                .Ancestors()
                .FirstOrDefault(candidate =>
                    candidate
                        is BaseMethodDeclarationSyntax
                            or LocalFunctionStatementSyntax
                            or AnonymousFunctionExpressionSyntax
                            or AccessorDeclarationSyntax
                )
            ?? invocation.Ancestors().OfType<CompilationUnitSyntax>().FirstOrDefault();
        if (scope is null)
            return false;

        var effective = scope
            .DescendantNodes(descendIntoChildren: child =>
                child == scope
                || child is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
            )
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment =>
                assignment.SpanStart < invocation.SpanStart
                && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && SymbolEqualityComparer.Default.Equals(
                    context
                        .SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken)
                        .Symbol,
                    blockingProperty
                )
                && AssignmentTargetsThisClient(assignment, context, tcpClientType, connectClient)
            )
            .OrderBy(assignment => assignment.SpanStart)
            .LastOrDefault();

        return effective != null
            && context.SemanticModel.GetConstantValue(effective.Right, context.CancellationToken)
                is { HasValue: true, Value: false }
            && !ReceiverWasReassignedAfter(
                scope,
                effective,
                invocation,
                context,
                tcpClientType,
                connectClient
            );
    }

    private static bool ReceiverWasReassignedAfter(
        SyntaxNode scope,
        AssignmentExpressionSyntax blockingAssignment,
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol tcpClientType,
        ReceiverRef connectClient
    )
    {
        var nodes = scope.DescendantNodes(descendIntoChildren: child =>
            child == scope
            || child is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
        );

        if (
            nodes
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment =>
                    assignment.SpanStart > blockingAssignment.SpanStart
                    && assignment.SpanStart < invocation.SpanStart
                    && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    && WritesReceiver(assignment, context, tcpClientType, connectClient)
                )
        )
            return true;

        return nodes
            .OfType<ArgumentSyntax>()
            .Any(argument =>
                argument.SpanStart > blockingAssignment.SpanStart
                && argument.SpanStart < invocation.SpanStart
                && (
                    argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                    || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
                )
                && SameReceiver(
                    new ReceiverRef(argument.Expression, implicitThis: false),
                    connectClient,
                    context
                )
            );
    }

    private static bool WritesReceiver(
        AssignmentExpressionSyntax assignment,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol tcpClientType,
        ReceiverRef connectClient
    )
    {
        var left = Unwrap(assignment.Left);
        if (SameReceiver(new ReceiverRef(left, implicitThis: false), connectClient, context))
            return true;

        if (TupleWritesReceiver(left, context, connectClient))
            return true;

        return TryGetClientPropertyOwner(left, context, tcpClientType) is { } owner
            && SameReceiver(owner, connectClient, context);
    }

    private static bool TupleWritesReceiver(
        ExpressionSyntax expression,
        SyntaxNodeAnalysisContext context,
        ReceiverRef connectClient
    )
    {
        expression = Unwrap(expression);
        if (expression is not TupleExpressionSyntax tuple)
            return false;

        return tuple.Arguments.Any(argument =>
            SameReceiver(
                new ReceiverRef(argument.Expression, implicitThis: false),
                connectClient,
                context
            ) || TupleWritesReceiver(argument.Expression, context, connectClient)
        );
    }

    private static bool AssignmentTargetsThisClient(
        AssignmentExpressionSyntax assignment,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol tcpClientType,
        ReceiverRef connectClient
    )
    {
        var left = Unwrap(assignment.Left);
        if (left is MemberAccessExpressionSyntax blockingAccess)
        {
            return TryGetClientPropertyOwner(
                    Unwrap(blockingAccess.Expression),
                    context,
                    tcpClientType
                )
                    is { } assignedClient
                && SameReceiver(assignedClient, connectClient, context);
        }

        return TryGetNestedInitializerReceiver(assignment, context, tcpClientType)
                is { } initializerClient
            && SameReceiver(initializerClient, connectClient, context);
    }

    private static ReceiverRef? TryGetNestedInitializerReceiver(
        AssignmentExpressionSyntax blockingAssignment,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol tcpClientType
    )
    {
        if (blockingAssignment.Parent is not InitializerExpressionSyntax socketInitializer)
            return null;
        if (socketInitializer.Parent is not AssignmentExpressionSyntax clientAssignment)
            return null;
        if (!IsTcpClientClientProperty(Unwrap(clientAssignment.Left), context, tcpClientType))
            return null;
        if (clientAssignment.Parent is not InitializerExpressionSyntax tcpInitializer)
            return null;
        if (tcpInitializer.Parent is not BaseObjectCreationExpressionSyntax creation)
            return null;

        var createdType = context
            .SemanticModel.GetTypeInfo(creation, context.CancellationToken)
            .Type;
        if (!IsTcpClientOrDerived(createdType, tcpClientType))
            return null;

        return creation.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
                when context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken)
                    is { } declared => new ReceiverRef(null, implicitThis: false, declared),
            AssignmentExpressionSyntax target
                when target.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    && Unwrap(target.Right) == creation => new ReceiverRef(
                target.Left,
                implicitThis: false
            ),
            _ => null,
        };
    }

    private static ReceiverRef? GetConnectReceiver(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => new ReceiverRef(
                memberAccess.Expression,
                implicitThis: false
            ),
            MemberBindingExpressionSyntax
                when invocation.Parent is ConditionalAccessExpressionSyntax conditional =>
                new ReceiverRef(conditional.Expression, implicitThis: false),
            IdentifierNameSyntax => new ReceiverRef(null, implicitThis: true),
            _ => null,
        };

    private static ReceiverRef? TryGetClientPropertyOwner(
        ExpressionSyntax expression,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol tcpClientType
    )
    {
        if (IsTcpClientClientProperty(expression, context, tcpClientType))
        {
            return Unwrap(expression) is MemberAccessExpressionSyntax memberAccess
                ? new ReceiverRef(memberAccess.Expression, implicitThis: false)
                : new ReceiverRef(null, implicitThis: true);
        }

        return null;
    }

    private static bool IsTcpClientClientProperty(
        ExpressionSyntax expression,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol tcpClientType
    ) =>
        GetSymbol(context, Unwrap(expression)) is IPropertySymbol clientProperty
        && clientProperty.Name == "Client"
        && SymbolEqualityComparer.Default.Equals(clientProperty.ContainingType, tcpClientType);

    private static bool SameReceiver(
        ReceiverRef left,
        ReceiverRef right,
        SyntaxNodeAnalysisContext context
    )
    {
        if (left.ImplicitThis && right.ImplicitThis)
            return true;

        if (left.ImplicitThis)
            return IsCurrentInstance(right.Expression);

        if (right.ImplicitThis)
            return IsCurrentInstance(left.Expression);

        if (
            left.Expression != null
            && right.Expression != null
            && ExpressionsReferToSameInstance(left.Expression, right.Expression, context)
        )
            return true;

        var leftSymbol = left.DeclaredSymbol ?? GetSimpleReceiverSymbol(left.Expression, context);
        var rightSymbol =
            right.DeclaredSymbol ?? GetSimpleReceiverSymbol(right.Expression, context);
        return leftSymbol != null
            && rightSymbol != null
            && SymbolEqualityComparer.Default.Equals(leftSymbol, rightSymbol);
    }

    private static ISymbol? GetSimpleReceiverSymbol(
        ExpressionSyntax? expression,
        SyntaxNodeAnalysisContext context
    )
    {
        if (expression is null)
            return null;

        expression = Unwrap(expression);
        ISymbol? symbol = expression switch
        {
            IdentifierNameSyntax => GetSymbol(context, expression),
            MemberAccessExpressionSyntax memberAccess
                when Unwrap(memberAccess.Expression)
                    is ThisExpressionSyntax
                        or BaseExpressionSyntax => GetSymbol(context, memberAccess.Name),
            _ => null,
        };

        return symbol is ILocalSymbol or IParameterSymbol or IFieldSymbol ? symbol : null;
    }

    private static bool ExpressionsReferToSameInstance(
        ExpressionSyntax? left,
        ExpressionSyntax? right,
        SyntaxNodeAnalysisContext context
    )
    {
        if (left is null || right is null)
            return false;

        left = Unwrap(left);
        right = Unwrap(right);

        if (IsCurrentInstance(left) && IsCurrentInstance(right))
            return true;

        if (
            left is MemberAccessExpressionSyntax leftThis
            && Unwrap(leftThis.Expression) is ThisExpressionSyntax
        )
            return ExpressionsReferToSameInstance(leftThis.Name, right, context);

        if (
            right is MemberAccessExpressionSyntax rightThis
            && Unwrap(rightThis.Expression) is ThisExpressionSyntax
        )
            return ExpressionsReferToSameInstance(left, rightThis.Name, context);

        if (left is IdentifierNameSyntax && right is IdentifierNameSyntax)
        {
            var leftSymbol = GetSymbol(context, left);
            var rightSymbol = GetSymbol(context, right);
            return leftSymbol != null
                && rightSymbol != null
                && SymbolEqualityComparer.Default.Equals(leftSymbol, rightSymbol);
        }

        if (
            left is MemberAccessExpressionSyntax leftMember
            && right is MemberAccessExpressionSyntax rightMember
        )
        {
            var leftSymbol = GetSymbol(context, left);
            var rightSymbol = GetSymbol(context, right);
            return leftSymbol != null
                && rightSymbol != null
                && SymbolEqualityComparer.Default.Equals(leftSymbol, rightSymbol)
                && ExpressionsReferToSameInstance(
                    leftMember.Expression,
                    rightMember.Expression,
                    context
                );
        }

        return false;
    }

    private static bool IsSimpleReceiver(ReceiverRef receiver, SyntaxNodeAnalysisContext context) =>
        receiver.ImplicitThis
        || receiver.DeclaredSymbol != null
        || IsCurrentInstance(receiver.Expression)
        || GetSimpleReceiverSymbol(receiver.Expression, context) != null;

    private static bool IsCurrentInstance(ExpressionSyntax? expression) =>
        expression is ThisExpressionSyntax or BaseExpressionSyntax;

    private static bool IsTcpClientOrDerived(ITypeSymbol? type, INamedTypeSymbol tcpClientType)
    {
        while (type != null)
        {
            if (SymbolEqualityComparer.Default.Equals(type, tcpClientType))
                return true;
            type = type.BaseType;
        }

        return false;
    }

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
