using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking
/// <c>System.Net.Mail.SmtpClient.Send</c> inside async code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC049
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// <c>SmtpClient.Send</c> parks a thread-pool thread on an SMTP
/// handshake. That wait is not a <c>CancellationToken</c>.
/// <c>SendMailAsync</c> yields the thread. Token-taking
/// <c>SendMailAsync</c> is .NET 5+; .NET Framework has the tokenless
/// form. The TAP counterpart is <c>SendMailAsync</c>, not the
/// event-based <c>SendAsync</c>.
/// </para>
/// <para>
/// <b>Why this is not CC004:</b> CC004 is <c>HttpClient</c>.
/// <c>SmtpClient.Send</c> produced zero diagnostics from every shipped
/// rule — verified empirically. <c>Send</c> is not virtual; <c>new</c>
/// hiders match by inheritance plus the framework shape
/// (<c>MailMessage</c> or four strings).
/// </para>
/// <para>
/// Analyzer-only in this slice: a mechanical rewrite is a follow-up.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
/// {
///     client.Send(message);   // CC049
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingSmtpClientAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC049";

    private static readonly LocalizableString Title =
        "Avoid blocking SmtpClient.Send in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking 'SmtpClient.{0}' in async code; use 'SendMailAsync'";
    private static readonly LocalizableString Description =
        "SmtpClient.Send parks a thread-pool thread on an SMTP handshake; in async code use SendMailAsync. Token-taking SendMailAsync is .NET 5+; .NET Framework has the tokenless form. Do not use the event-based SendAsync.";
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
            var clientType = start.Compilation.GetTypeByMetadataName("System.Net.Mail.SmtpClient");
            if (clientType is null)
                return;

            var messageType = start.Compilation.GetTypeByMetadataName(
                "System.Net.Mail.MailMessage"
            );

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, clientType, messageType),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol clientType,
        INamedTypeSymbol? messageType
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
        if (invokedName is null || invokedName.Identifier.Text != "Send")
            return;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        if (!IsFrameworkSend(method, clientType, messageType))
            return;

        if (clientType.GetMembers("SendMailAsync").IsEmpty)
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invokedName.GetLocation(), method.Name));
    }

    /// <summary>
    /// Match the framework <c>Send</c> overloads: instance, arity 0,
    /// <c>void</c>, either one <c>MailMessage</c> or four strings, on
    /// <c>SmtpClient</c> or a subclass. Custom helpers, generics, and
    /// statics stay quiet. <c>SendAsync</c> is a different name.
    /// </summary>
    private static bool IsFrameworkSend(
        IMethodSymbol method,
        INamedTypeSymbol clientType,
        INamedTypeSymbol? messageType
    )
    {
        if (method.IsStatic || method.Arity != 0)
            return false;

        if (!IsOrInherits(method.ContainingType, clientType))
            return false;

        if (method.ReturnType.SpecialType != SpecialType.System_Void)
            return false;

        if (method.Parameters.Length == 1)
        {
            return messageType is not null
                && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, messageType);
        }

        if (method.Parameters.Length != 4)
            return false;

        return method.Parameters.All(p => p.Type.SpecialType == SpecialType.System_String);
    }

    private static bool IsOrInherits(INamedTypeSymbol? type, INamedTypeSymbol expected)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expected))
                return true;
        }

        return false;
    }
}
