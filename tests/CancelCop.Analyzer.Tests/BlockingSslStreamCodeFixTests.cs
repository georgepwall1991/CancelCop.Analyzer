using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC051 fixer: rewritten code is compiled by the harness.
/// <c>AuthenticateAsClient</c> → <c>await AuthenticateAsClientAsync</c>.
/// </summary>
/// <remarks>
/// The only token-taking <c>AuthenticateAsClientAsync</c> overload is the
/// <see cref="System.Net.Security.SslClientAuthenticationOptions"/> arity —
/// no <c>(string..., CancellationToken)</c> forms exist in the net9 ref
/// pack. A string-arity call therefore rewrites tokenless even with a token
/// in scope; token flow is exercised on the options shape (whose trailing
/// token parameter is optional, but which the analyzer still validates by
/// speculative rebind).
/// </remarks>
public class BlockingSslStreamCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingSslStreamAnalyzer,
        BlockingSslStreamCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingSslStreamAnalyzer,
            BlockingSslStreamCodeFixProvider,
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
        new DiagnosticResult("CC051", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("AuthenticateAsClient");

    [Fact]
    public async Task SslStreamAuthenticateAsClient_WithoutMatchingTokenArity_RewritesTokenless()
    {
        // No `AuthenticateAsClientAsync(string, CancellationToken)` exists;
        // the rewrite stays honest and drops the in-scope token.
        var test =
            @"
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream stream, CancellationToken cancellationToken)
    {
        stream.{|#0:AuthenticateAsClient|}(""host"");
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream stream, CancellationToken cancellationToken)
    {
        await stream.AuthenticateAsClientAsync(""host"");
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task SslStreamAuthenticateAsClient_OptionsShape_FlowsTheToken()
    {
        // Only the SslClientAuthenticationOptions arity accepts a token.
        var test =
            @"
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream stream, CancellationToken cancellationToken)
    {
        stream.{|#0:AuthenticateAsClient|}(new SslClientAuthenticationOptions { TargetHost = ""host"" });
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream stream, CancellationToken cancellationToken)
    {
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = ""host"" }, cancellationToken);
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task SslStreamAuthenticateAsClient_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Net.Security;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream stream)
    {
        stream.{|#0:AuthenticateAsClient|}(""host"");
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Security;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream stream)
    {
        await stream.AuthenticateAsClientAsync(""host"");
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task SslStreamAuthenticateAsClient_NullConditional_HoistsToIfNotNull()
    {
        // The whole null-conditional statement hoists; the string arity has
        // no token-taking form, so the hoisted rewrite stays tokenless even
        // though a token is in scope (the token-first candidate fails its
        // speculative rebind and the honest tokenless form wins).
        var source =
            @"
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream? stream, CancellationToken cancellationToken)
    {
        stream?.{|#0:AuthenticateAsClient|}(""host"");
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream? stream, CancellationToken cancellationToken)
    {
        if (stream is not null)
        {
            await stream.AuthenticateAsClientAsync(""host"");
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task SslStreamAuthenticateAsClient_NamedOptionsSpine_HoistsWithNamedToken()
    {
        // The spine diagnostic carries the token metadata even though no
        // in-place rebind exists, so the statement hoist can offer a
        // named-token candidate that its speculative rebind validates
        // against the options arity. The original argument is spelled as an
        // in-order named argument; the token is appended as a named argument.
        var source =
            @"
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream? stream, CancellationToken cancellationToken)
    {
        stream?.{|#0:AuthenticateAsClient|}(
            sslClientAuthenticationOptions: new SslClientAuthenticationOptions { TargetHost = ""host"" });
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(SslStream? stream, CancellationToken cancellationToken)
    {
        if (stream is not null)
        {
            await stream.AuthenticateAsClientAsync(
            sslClientAuthenticationOptions: new SslClientAuthenticationOptions { TargetHost = ""host"" }, cancellationToken: cancellationToken);
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task SslStreamAuthenticateAsClient_InsideLock_ReportsWithoutOfferingAFix()
    {
        // await-unsafe outranks every other reason; the hoist would land its
        // if-statement in the same lock body, where await stays illegal.
        var source =
            @"
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object sync = new();

    public async Task RunAsync(SslStream stream, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            stream?.{|#0:AuthenticateAsClient|}(""host"");
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task AuthenticateAsClient_ThisAliasInsideAuthenticateAsClientAsync_NoFix()
    {
        // A receiver provably assigned from `this` virtually dispatches the rewrite back
        // to the enclosing member — implicit-this recursion through an alias.
        var source =
            @"
using System.IO;
using System.Net.Security;
using System.Threading.Tasks;

public class Client : SslStream
{
    public Client()
        : base(Stream.Null) { }

    public async Task<bool> AuthenticateAsClientAsync(string host)
    {
        SslStream self = this;
        self.{|#0:AuthenticateAsClient|}(host);
        return true;
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task AuthenticateAsClient_ConditionalThisAliasInsideAuthenticateAsClientAsync_NoFix()
    {
        // `self?.AuthenticateAsClient(...)` on a spine whose operation is provably
        // `this` dispatches the hoisted call back into the enclosing member.
        var source =
            @"
using System.IO;
using System.Net.Security;
using System.Threading.Tasks;

public class Client : SslStream
{
    public Client()
        : base(Stream.Null) { }

    public async Task<bool> AuthenticateAsClientAsync(string host)
    {
        SslStream self = this;
        self?.{|#0:AuthenticateAsClient|}(host);
        return true;
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task AuthenticateAsClient_ConditionalSpineWithHider_NoFix()
    {
        // A derived type hides the TAP member with a same-named `new` method. The spine
        // hoist must not bind to it: only the framework's own
        // SslStream.AuthenticateAsClientAsync validates.
        var source =
            @"
using System.IO;
using System.Net.Security;
using System.Threading.Tasks;

public class Derived : SslStream
{
    public Derived()
        : base(Stream.Null) { }

    public new async Task AuthenticateAsClientAsync(string host)
    {
        await Task.Yield();
    }
}

public class TestClass
{
    public async Task RunAsync(Derived? stream)
    {
        stream?.{|#0:AuthenticateAsClient|}(""host"");
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }
}
