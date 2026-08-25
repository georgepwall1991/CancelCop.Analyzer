using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC049 fixer: rewritten code is compiled by the harness.
/// <c>Send</c> → <c>await SendMailAsync</c> (not event-based <c>SendAsync</c>).
/// </summary>
public class BlockingSmtpClientCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingSmtpClientAnalyzer,
        BlockingSmtpClientCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingSmtpClientAnalyzer,
            BlockingSmtpClientCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Expected(int location = 0) =>
        new DiagnosticResult("CC049", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("Send");

    [Fact]
    public async Task SendMailMessage_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
    {
        client.{|#0:Send|}(message);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
    {
        await client.SendMailAsync(message, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task SendMailMessage_NamedArgument_NamesTheToken()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
    {
        client.{|#0:Send|}(message: message);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
    {
        await client.SendMailAsync(message: message, cancellationToken: cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task SendMailMessage_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient client, MailMessage message)
    {
        client.{|#0:Send|}(message);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Mail;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient client, MailMessage message)
    {
        await client.SendMailAsync(message);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task SendFourStrings_WithTokenInScope_FlowsTheToken()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient client, CancellationToken cancellationToken)
    {
        client.{|#0:Send|}(""from@example.com"", ""to@example.com"", ""s"", ""b"");
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient client, CancellationToken cancellationToken)
    {
        await client.SendMailAsync(""from@example.com"", ""to@example.com"", ""s"", ""b"", cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task Send_NullConditional_HoistsToIfNotNullSendMailAsync()
    {
        // The whole null-conditional statement hoists; the in-scope token flows into the call.
        var source =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient? client, MailMessage message, CancellationToken cancellationToken)
    {
        client?.{|#0:Send|}(message);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient? client, MailMessage message, CancellationToken cancellationToken)
    {
        if (client is not null)
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task Send_InsideLock_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object _gate = new object();

    public async Task RunAsync(SmtpClient client, MailMessage message, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            client.{|#0:Send|}(message);
        }

        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task OptionalIntSendMailAsyncHider_WithoutToken_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Mail;
using System.Threading.Tasks;

public class HiddenClient : SmtpClient
{
    public new Task SendMailAsync(MailMessage message, int retries = 0) => Task.CompletedTask;
}

public class TestClass
{
    public async Task RunAsync(HiddenClient client, MailMessage message)
    {
        client.{|#0:Send|}(message);
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task SubclassHidingSendMailAsync_ShouldNotReport()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class HiddenClient : SmtpClient
{
    public new int SendMailAsync(MailMessage message) => 0;
    public new int SendMailAsync(MailMessage message, CancellationToken cancellationToken) => 0;
}

public class TestClass
{
    public async Task RunAsync(HiddenClient client, MailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Send(message);
        await Task.Yield();
    }
}";

        var t = new CSharpAnalyzerTest<BlockingSmtpClientAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await t.RunAsync();
    }

    [Fact]
    public async Task UnrelatedSendMailAsyncHelper_StillFixes()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : SmtpClient
{
    public async Task SendMailAsync(string host, MailMessage message, CancellationToken cancellationToken)
    {
        {|#0:Send|}(message);
    }
}";

        var fixedCode =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : SmtpClient
{
    public async Task SendMailAsync(string host, MailMessage message, CancellationToken cancellationToken)
    {
        await SendMailAsync(message, cancellationToken);
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ExtraSameArityTokenOverload_NamedMessage_StillUsesFrameworkTokenName()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class ExtraClient : SmtpClient
{
    public Task SendMailAsync(string host, CancellationToken ct) => Task.CompletedTask;
}

public class TestClass
{
    public async Task RunAsync(ExtraClient client, MailMessage message, CancellationToken cancellationToken)
    {
        client.{|#0:Send|}(message: message);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class ExtraClient : SmtpClient
{
    public Task SendMailAsync(string host, CancellationToken ct) => Task.CompletedTask;
}

public class TestClass
{
    public async Task RunAsync(ExtraClient client, MailMessage message, CancellationToken cancellationToken)
    {
        await client.SendMailAsync(message: message, cancellationToken: cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task ComposedClientField_StillFixes()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : SmtpClient
{
    private SmtpClient _other = null!;

    public new async Task SendMailAsync(MailMessage message, CancellationToken cancellationToken)
    {
        _other.{|#0:Send|}(message);
    }
}";

        var fixedCode =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : SmtpClient
{
    private SmtpClient _other = null!;

    public new async Task SendMailAsync(MailMessage message, CancellationToken cancellationToken)
    {
        await _other.SendMailAsync(message, cancellationToken);
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task FieldAssignedFromLocalThisAlias_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : SmtpClient
{
    private SmtpClient _self;

    public ProviderClient()
    {
        SmtpClient self = this;
        _self = self;
    }

    public new async Task SendMailAsync(MailMessage message, CancellationToken cancellationToken)
    {
        _self.{|#0:Send|}(message);
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task PropertyReturningThis_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : SmtpClient
{
    private SmtpClient Self => this;

    public new async Task SendMailAsync(MailMessage message, CancellationToken cancellationToken)
    {
        Self.{|#0:Send|}(message);
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task FieldAssignedThisInConstructor_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : SmtpClient
{
    private SmtpClient _self;

    public ProviderClient()
    {
        _self = this;
    }

    public new async Task SendMailAsync(MailMessage message, CancellationToken cancellationToken)
    {
        _self.{|#0:Send|}(message);
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task CallFromSendMailAsync_ReportsWithoutOfferingAFix()
    {
        var source =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class ProviderClient : SmtpClient
{
    public new async Task SendMailAsync(MailMessage message)
    {
        {|#0:Send|}(message);
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task FixAll_TwoSends_BothBecomeAwaitSendMailAsync()
    {
        var test =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient client, MailMessage a, MailMessage b, CancellationToken cancellationToken)
    {
        client.{|#0:Send|}(a);
        client.{|#1:Send|}(b);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient client, MailMessage a, MailMessage b, CancellationToken cancellationToken)
    {
        await client.SendMailAsync(a, cancellationToken);
        await client.SendMailAsync(b, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected(0), Expected(1)).RunAsync();
    }
    [Fact]
    public async Task ConditionalSend_HoistsToIfNotNullSendMailAsync()
    {
        var test =
            @"
using System;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient? client, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var message = new MailMessage();
        client?.{|#0:Send|}(message);
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SmtpClient? client, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var message = new MailMessage();
        if (client is not null)
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC049", DiagnosticSeverity.Warning).WithLocation(0);
        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task ConditionalSendWithHiddenSendMailAsync_ReportsWithoutOfferingAFix()
    {
        // A subclass hiding SendMailAsync with a non-awaitable member would make the rewrite
        // non-compiling; the speculative rebind withholds it.
        var test =
            @"
using System.Net.Mail;
using System.Threading.Tasks;

public class HidingClient : SmtpClient
{
    public new int SendMailAsync(MailMessage message) => 0;
}

public class TestClass
{
    public async Task RunAsync(HidingClient? client)
    {
        await Task.Yield();
        var message = new MailMessage();
        client?.{|#0:Send|}(message);
        await Task.Yield();
    }
}";

        var expected = new DiagnosticResult("CC049", DiagnosticSeverity.Warning).WithLocation(0);
        await CreateTest(test, test, expected).RunAsync();
    }
}
