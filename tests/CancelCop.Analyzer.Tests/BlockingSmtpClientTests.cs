using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC049: blocking <c>System.Net.Mail.SmtpClient.Send</c> in async code.
/// <c>Send</c> is not virtual. The TAP counterpart is <c>SendMailAsync</c>,
/// not the event-based <c>SendAsync</c>. Token-taking
/// <c>SendMailAsync</c> is .NET 5+; Framework has the tokenless form.
/// </summary>
public class BlockingSmtpClientTests
{
    private sealed class AllAnalyzersTest
        : CSharpAnalyzerTest<MissingCancellationTokenAnalyzer, DefaultVerifier>
    {
        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() =>
            typeof(MissingCancellationTokenAnalyzer)
                .Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!);
    }

    private static DiagnosticResult Expected() =>
        new DiagnosticResult("CC049", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Send");

    [Fact]
    public async Task Send_MailMessage_InAsyncMethod_IsMissedByEveryShippedRule()
    {
        // Empirical gap: Send parks a pool thread on an SMTP handshake.
        // CC002 requires a token overload of the invoked method, and Send
        // has none. SendMailAsync is a differently named TAP counterpart.
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.{|#0:Send|}(message);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Send_FourStrings_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(SmtpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.{|#0:Send|}(""from@example.com"", ""to@example.com"", ""s"", ""b"");
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Send_InAsyncLambda_ShouldReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public void Run(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
    {
        Func<Task> work = async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            client.{|#0:Send|}(message);
            await Task.Yield();
        };
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Send_InSyncLambdaInsideAsyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Action send = () => client.Send(message);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task Send_NullConditional_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client?.{|#0:Send|}(message);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Send_InSyncMethod_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Mail;

public class Work
{
    public void Run(SmtpClient client, MailMessage message)
    {
        client.Send(message);
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task Send_HidingNewOnSubclass_ShouldReportDiagnostic()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class HiddenClient : SmtpClient
{
    public new void Send(MailMessage message) { }
}

public class Work
{
    public async Task RunAsync(HiddenClient client, MailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.{|#0:Send|}(message);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        t.ExpectedDiagnostics.Add(Expected());
        await t.RunAsync();
    }

    [Fact]
    public async Task Send_CustomOverloadOnSubclass_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class OptionsClient : SmtpClient
{
    public void Send(int timeout) { }
}

public class Work
{
    public async Task RunAsync(OptionsClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Send(30);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task Send_GenericHelperOnSubclass_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class HiddenClient : SmtpClient
{
    public void Send<T>(MailMessage message) { }
}

public class Work
{
    public async Task RunAsync(HiddenClient client, MailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Send<int>(message);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task Send_StaticHelperOnSubclass_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class HiddenClient : SmtpClient
{
    public static new void Send(MailMessage message) { }
}

public class Work
{
    public async Task RunAsync(HiddenClient client, MailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HiddenClient.Send(message);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task LookalikeSend_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Threading.Tasks;

public class SmtpClient
{
    public void Send(object message) { }
}

public class Work
{
    public async Task RunAsync(SmtpClient client, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Send(null!);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task SendMailAsync_ShouldNotReportDiagnostic()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
    {
        await client.SendMailAsync(message, cancellationToken);
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task SendAsync_EventBased_ShouldNotReportDiagnostic()
    {
        // SendAsync is EAP, not the TAP counterpart. Recommending it would
        // be wrong; the rule must stay quiet rather than rewrite toward it.
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class Work
{
    public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.SendAsync(message, null);
        await Task.Yield();
    }
}";

        var t = new AllAnalyzersTest
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }
}
