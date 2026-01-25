// =============================================================================
// CC011: Avoid blocking on async code
// =============================================================================
//
// WHY THIS MATTERS:
// Blocking on async code (using .Wait(), .Result, or .GetAwaiter().GetResult())
// can cause serious problems:
// - DEADLOCKS: In UI apps and ASP.NET Classic, blocking can deadlock forever
// - THREAD STARVATION: Blocks the thread pool, reducing scalability
// - WASTED RESOURCES: Ties up a thread doing nothing while waiting
// - DEFEATS ASYNC: You lose all benefits of async by blocking
//
// THE RULE:
// - Never use .Wait(), .Result, or .GetAwaiter().GetResult() on tasks
// - Use await instead to properly handle async operations
// - If you must block, consider Task.Run().GetAwaiter().GetResult() (still not ideal)
// - Better yet, make the calling code async all the way up
//
// =============================================================================

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC011: Avoid blocking on async code.
/// </summary>
public class CC011_BlockingCalls
{
    // -------------------------------------------------------------------------
    // VIOLATIONS (CC011 will warn on these)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC011 WARNING: Using .Result blocks the thread and can deadlock.
    /// In a UI app, this WILL deadlock because the continuation needs
    /// the UI thread which is blocked waiting for the result.
    /// </summary>
    public int GetDataBlocking()
    {
        // DANGEROUS: Can deadlock in synchronization contexts
        return GetDataAsync().Result;
    }

    /// <summary>
    /// CC011 WARNING: .Wait() is equally problematic.
    /// </summary>
    public void ProcessBlocking()
    {
        // DANGEROUS: Blocks the thread
        ProcessAsync().Wait();
    }

    /// <summary>
    /// CC011 WARNING: GetAwaiter().GetResult() also blocks.
    /// Some developers think this is safer than .Result - it's not!
    /// The only difference is exception handling (no AggregateException).
    /// </summary>
    public int GetDataWithGetResult()
    {
        // STILL DANGEROUS: Just as bad for deadlocks
        return GetDataAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// CC011 WARNING: Task.WaitAll blocks on multiple tasks.
    /// </summary>
    public void ProcessMultipleBlocking()
    {
        var task1 = Task.Delay(1000);
        var task2 = Task.Delay(2000);

        // DANGEROUS: Blocks waiting for all tasks
        Task.WaitAll(task1, task2);
    }

    /// <summary>
    /// CC011 WARNING: Task.WaitAny also blocks.
    /// </summary>
    public int WaitForFirstBlocking()
    {
        var task1 = Task.Delay(1000);
        var task2 = Task.Delay(2000);

        // DANGEROUS: Blocks waiting for any task
        return Task.WaitAny(task1, task2);
    }

    /// <summary>
    /// CC011 WARNING: Multiple blocking calls compound the problem.
    /// </summary>
    public void MultipleBlockingCalls()
    {
        ProcessAsync().Wait();           // Warning 1
        var result = GetDataAsync().Result;  // Warning 2
        Console.WriteLine(result);
    }

    // -------------------------------------------------------------------------
    // CORRECT PATTERNS (No warnings)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CORRECT: Use await to get the result asynchronously.
    /// </summary>
    public async Task<int> GetDataCorrectlyAsync()
    {
        return await GetDataAsync();
    }

    /// <summary>
    /// CORRECT: Use await instead of Wait().
    /// </summary>
    public async Task ProcessCorrectlyAsync()
    {
        await ProcessAsync();
    }

    /// <summary>
    /// CORRECT: Use Task.WhenAll for multiple tasks.
    /// </summary>
    public async Task ProcessMultipleCorrectlyAsync()
    {
        var task1 = Task.Delay(1000);
        var task2 = Task.Delay(2000);

        // Awaits without blocking
        await Task.WhenAll(task1, task2);
    }

    /// <summary>
    /// CORRECT: Use Task.WhenAny for racing tasks.
    /// </summary>
    public async Task<Task> WaitForFirstCorrectlyAsync()
    {
        var task1 = Task.Delay(1000);
        var task2 = Task.Delay(2000);

        // Returns the first completed task without blocking
        return await Task.WhenAny(task1, task2);
    }

    /// <summary>
    /// ACCEPTABLE: Blocking in Main() entry point of console apps.
    /// This is sometimes necessary for the program entry point.
    /// Better yet, use async Main in modern C#.
    /// </summary>
    public static void LegacyMain(string[] args)
    {
        // In console apps, this usually won't deadlock
        // because there's no SynchronizationContext
        // But still prefer async Main
        MainAsync(args).GetAwaiter().GetResult();
    }

    /// <summary>
    /// BEST: Use async Main in modern C# (C# 7.1+).
    /// </summary>
    public static async Task ModernMainAsync(string[] args)
    {
        await ProcessAsync();
        Console.WriteLine("Done!");
    }

    // Helper methods for examples
    private static Task ProcessAsync() => Task.Delay(1000);
    private static Task<int> GetDataAsync() => Task.FromResult(42);
    private static Task MainAsync(string[] args) => Task.CompletedTask;
}
