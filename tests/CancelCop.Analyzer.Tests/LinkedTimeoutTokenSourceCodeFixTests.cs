using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    CancelCop.Analyzer.LinkedTimeoutTokenSourceAnalyzer,
    CancelCop.Analyzer.LinkedTimeoutTokenSourceCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class LinkedTimeoutTokenSourceCodeFixTests
{
    [Fact]
    public async Task TimeSpanCtor_RewritesToLinkedPlusCancelAfter()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = {|#0:new CancellationTokenSource(TimeSpan.FromSeconds(30))|};
        await DoAsync(cts.Token);
    }
}";

        var fixedCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        await DoAsync(cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC029")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task IntCtor_PreservesDelayExpression()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = {|#0:new CancellationTokenSource(5000)|};
        await DoAsync(cts.Token);
    }
}";

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(5000);
        await DoAsync(cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC029")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ParameterlessPlusCancelAfter_RewritesCreationOnly()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = new CancellationTokenSource();
        cts.{|#0:CancelAfter|}(TimeSpan.FromSeconds(30));
        await DoAsync(cts.Token);
    }
}";

        var fixedCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        await DoAsync(cts.Token);
    }
}";

        var expected = VerifyCS.Diagnostic("CC029")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task NonUsing_PreservesNonUsingDeclaration()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cts = {|#0:new CancellationTokenSource(TimeSpan.FromSeconds(30))|};
        await DoAsync(cts.Token);
        cts.Dispose();
    }
}";

        var fixedCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        await DoAsync(cts.Token);
        cts.Dispose();
    }
}";

        var expected = VerifyCS.Diagnostic("CC029")
            .WithLocation(0)
            .WithArguments("cancellationToken");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task FixAll_TwoTimeoutSources_BothRewritten()
    {
        var test = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var a = {|#0:new CancellationTokenSource(TimeSpan.FromSeconds(1))|};
        using var b = {|#1:new CancellationTokenSource(TimeSpan.FromSeconds(2))|};
        await DoAsync(a.Token);
        await DoAsync(b.Token);
    }
}";

        var fixedCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var a = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        a.CancelAfter(TimeSpan.FromSeconds(1));
        using var b = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        b.CancelAfter(TimeSpan.FromSeconds(2));
        await DoAsync(a.Token);
        await DoAsync(b.Token);
    }
}";

        var expected = new[]
        {
            VerifyCS.Diagnostic("CC029").WithLocation(0).WithArguments("cancellationToken"),
            VerifyCS.Diagnostic("CC029").WithLocation(1).WithArguments("cancellationToken"),
        };

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
