using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// The harness compiles the fixed output, so an initializer that does not bind fails here.
/// </summary>
public class ParallelOptionsTokenCodeFixTests
{
    private static CSharpCodeFixTest<
        ParallelOptionsTokenAnalyzer,
        ParallelOptionsTokenCodeFixProvider,
        DefaultVerifier
    > CreateTest(string testCode, string fixedCode, string token = "cancellationToken")
    {
        var test = new CSharpCodeFixTest<
            ParallelOptionsTokenAnalyzer,
            ParallelOptionsTokenCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("CC034", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments(token)
        );
        return test;
    }

    [Fact]
    public async Task AppendsToAnExistingInitializer()
    {
        // Appending rather than replacing keeps whatever the author already set.
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = {|#0:new ParallelOptions { MaxDegreeOfParallelism = 4 }|};
        Parallel.ForEach(items, options, i => { });
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken };
        Parallel.ForEach(items, options, i => { });
    }
}";

        await CreateTest(test, fixedCode).RunAsync();
    }

    [Fact]
    public async Task CreatesAnInitializerAndDropsTheEmptyArgumentList()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = {|#0:new ParallelOptions()|};
        Parallel.ForEach(items, options, i => { });
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions { CancellationToken = cancellationToken };
        Parallel.ForEach(items, options, i => { });
    }
}";

        await CreateTest(test, fixedCode).RunAsync();
    }

    [Fact]
    public async Task FixesAnInlineOptionsArgument()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        Parallel.ForEach(items, {|#0:new ParallelOptions { MaxDegreeOfParallelism = 4 }|}, i => { });
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken cancellationToken)
    {
        Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, i => { });
    }
}";

        await CreateTest(test, fixedCode).RunAsync();
    }

    [Fact]
    public async Task EscapesAKeywordTokenName()
    {
        var test =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken @event)
    {
        var options = {|#0:new ParallelOptions()|};
        Parallel.ForEach(items, options, i => { });
    }
}";

        var fixedCode =
            @"
using System.Threading;
using System.Threading.Tasks;

public class Runner
{
    public void Run(int[] items, CancellationToken @event)
    {
        var options = new ParallelOptions { CancellationToken = @event };
        Parallel.ForEach(items, options, i => { });
    }
}";

        await CreateTest(test, fixedCode, "event").RunAsync();
    }
}
