using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    CancelCop.Analyzer.SwallowedCancellationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace CancelCop.Analyzer.Tests;

public class SwallowedCancellationAnalyzerTests
{
    private const string Harness = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    private Task DoAsync() => Task.CompletedTask;
    private void DoSync() { }
";

    [Fact]
    public async Task CatchException_OverAwait_NoRethrow_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        {|#0:catch|} (Exception) { }
    }
}";

        var expected = VerifyCS.Diagnostic("CC019").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CatchAll_OverAwait_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        {|#0:catch|} { }
    }
}";

        var expected = VerifyCS.Diagnostic("CC019").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CatchException_OverAwaitForeach_ShouldReportDiagnostic()
    {
        var test = new CSharpAnalyzerTest<SwallowedCancellationAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
            TestCode = @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IAsyncEnumerable<int> source)
    {
        try
        {
            await foreach (var item in source)
            {
            }
        }
        {|#0:catch|} (Exception) { }
    }
}",
        };
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic("CC019").WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task CatchException_OverAwaitUsingDeclaration_ShouldReportDiagnostic()
    {
        var test = new CSharpAnalyzerTest<SwallowedCancellationAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
            TestCode = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IAsyncDisposable resource)
    {
        try
        {
            await using var owned = resource;
        }
        {|#0:catch|} (Exception) { }
    }
}",
        };
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic("CC019").WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task CatchException_OverAwaitUsingStatement_ShouldReportDiagnostic()
    {
        var test = new CSharpAnalyzerTest<SwallowedCancellationAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
            TestCode = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task RunAsync(IAsyncDisposable resource)
    {
        try
        {
            await using (resource)
            {
            }
        }
        {|#0:catch|} (Exception) { }
    }
}",
        };
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic("CC019").WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task CatchException_Rethrows_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (Exception) { throw; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchException_RethrowsExplicitly_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (Exception ex) { throw ex; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchException_RethrowsWrapped_ShouldNotReportDiagnostic()
    {
        // A catch that throws a new exception wrapping the caught one is treated as a rethrow: a
        // `throw` statement is present, so CC019 conservatively stays quiet (Info rule, low FP — it does
        // not try to judge whether wrapping changes the cancellation semantics).
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (Exception ex) { throw new InvalidOperationException(""wrap"", ex); }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchException_RethrowsOnlyUnrelatedException_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        {|#0:catch|} (Exception ex)
        {
            if (ex is InvalidOperationException)
                throw;
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC019").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CatchException_RethrowsCancellationException_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                throw;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchException_RethrowsNonCancellationExceptions_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        {|#0:catch|} (Exception ex)
        {
            if (ex is not OperationCanceledException)
                throw;
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC019").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CatchException_RethrowsExceptTaskCanceledException_ShouldReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        {|#0:catch|} (Exception ex)
        {
            if (ex is not TaskCanceledException)
                throw;
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC019").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CatchException_RethrowsCancellationButNotUnrelatedException_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (Exception ex)
        {
            if (ex is not InvalidOperationException)
                throw;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchException_RethrowsCancellationWithCustomLookalike_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

namespace Custom
{
    public sealed class OperationCanceledException : Exception { }
}

public class TestClass
{
    public async Task RunAsync()
    {
        try { await Task.Yield(); }
        catch (Exception ex)
        {
            if (ex is not Custom.OperationCanceledException)
                throw;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchException_RethrowsExceptCancellationSubtypeImplementingInterface_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Threading.Tasks;

public interface IMarker { }
public sealed class MarkedCancellationException : OperationCanceledException, IMarker { }

public class TestClass
{
    public async Task RunAsync()
    {
        try { await Task.Yield(); }
        {|#0:catch|} (Exception ex)
        {
            if (ex is not IMarker)
                throw;
        }
    }
}";

        var expected = VerifyCS.Diagnostic("CC019").WithLocation(0);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CatchException_WithFilter_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchSpecificException_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (System.IO.IOException) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchException_NoAwaitInTry_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public void Run()
    {
        try { DoSync(); }
        catch (Exception) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchException_AwaitOnlyInsideNestedFunctions_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public void Run()
    {
        try
        {
            Func<Task> deferred = async () => await DoAsync();
            async Task LocalAsync() => await DoAsync();
        }
        catch (Exception) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CatchOperationCanceled_ShouldNotReportDiagnostic()
    {
        var test = Harness + @"
    public async Task RunAsync()
    {
        try { await DoAsync(); }
        catch (OperationCanceledException) { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
