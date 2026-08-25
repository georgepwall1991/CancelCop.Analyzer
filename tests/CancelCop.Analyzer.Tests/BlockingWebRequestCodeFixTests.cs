using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// CC052 fixer: rewritten code is compiled by the harness.
/// <c>GetResponse</c> → <c>await GetResponseAsync</c>.
/// </summary>
/// <remarks>
/// The framework's only <c>GetResponseAsync</c> is parameterless — no arity
/// accepts a <c>CancellationToken</c>, so every rewrite stays tokenless even
/// with a token in scope (the token-first candidate fails its speculative
/// rebind and the honest tokenless form wins). The provably-fresh
/// exact-framework receiver case cannot be exercised: <c>WebRequest</c> is
/// abstract and every constructor in the <c>WebRequest</c>/<c>WebResponse</c>
/// family is family-only, so <c>new WebRequest(...)</c> is not compilable C#.
/// </remarks>
public class BlockingWebRequestCodeFixTests
{
    private static CSharpCodeFixTest<
        BlockingWebRequestAnalyzer,
        BlockingWebRequestCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<
            BlockingWebRequestAnalyzer,
            BlockingWebRequestCodeFixProvider,
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
        new DiagnosticResult("CC052", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments("GetResponse");

    [Fact]
    public async Task WebRequestGetResponse_WithTokenInScope_RewritesTokenless()
    {
        // No `GetResponseAsync(CancellationToken)` exists; the rewrite stays
        // honest and drops the in-scope token.
        var test =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(WebRequest request, CancellationToken cancellationToken)
    {
        request.{|#0:GetResponse|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(WebRequest request, CancellationToken cancellationToken)
    {
        await request.GetResponseAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task HttpWebRequestGetResponse_RewritesThroughOverrideChain()
    {
        // HttpWebRequest overrides GetResponse; the rewrite candidate must
        // still resolve to the framework's GetResponseAsync on WebRequest
        // through the override chain.
        var test =
            @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpWebRequest request)
    {
        request.{|#0:GetResponse|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(HttpWebRequest request)
    {
        await request.GetResponseAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task WebRequestGetResponse_WithoutTokenInScope_StillCompiles()
    {
        var test =
            @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(WebRequest request)
    {
        request.{|#0:GetResponse|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(WebRequest request)
    {
        await request.GetResponseAsync();
        await Task.Yield();
    }
}";

        await CreateTest(test, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task WebRequestGetResponse_NullConditional_HoistsToIfNotNull()
    {
        var source =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(WebRequest? request, CancellationToken cancellationToken)
    {
        request?.{|#0:GetResponse|}();
        await Task.Yield();
    }
}";

        var fixedCode =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(WebRequest? request, CancellationToken cancellationToken)
    {
        if (request is not null)
        {
            await request.GetResponseAsync();
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, fixedCode, Expected()).RunAsync();
    }

    [Fact]
    public async Task WebRequestGetResponse_InsideLock_ReportsWithoutOfferingAFix()
    {
        // await-unsafe outranks every other reason; the hoist would land its
        // if-statement in the same lock body, where await stays illegal.
        var source =
            @"
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private readonly object sync = new();

    public async Task RunAsync(WebRequest request, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            request.{|#0:GetResponse|}();
        }
        await Task.Yield();
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetResponse_BareInsideGetResponseAsyncMember_NoFix()
    {
        // A bare call inside a GetResponseAsync-shaped member is implicit-this
        // recursion — no rewrite.
        var source =
            @"
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;

public class Worker : FileWebRequest
{
    public Worker()
        : base(default, default) { }

    public async Task<bool> GetResponseAsync(bool verbose)
    {
        {|#0:GetResponse|}();
        return true;
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }

    [Fact]
    public async Task GetResponse_DerivedConstructionReceiverInsideGetResponseAsyncMember_Withheld()
    {
        // `new Worker(...)` may BE the enclosing instance — derived constructions are
        // not provably fresh.
        var source =
            @"
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;

public class Worker : FileWebRequest
{
    public Worker()
        : base(default, default) { }

    public Worker(bool verbose)
        : this() { }

    public async Task<bool> GetResponseAsync(bool flag)
    {
        new Worker(flag).{|#0:GetResponse|}();
        return true;
    }
}";

        await CreateTest(source, source, Expected()).RunAsync();
    }
}
