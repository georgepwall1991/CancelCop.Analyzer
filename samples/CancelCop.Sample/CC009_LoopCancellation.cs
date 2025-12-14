// =============================================================================
// CC009: Loops should check for cancellation
// =============================================================================
//
// WHY THIS MATTERS:
// Loops are where your code spends most of its time. A loop processing
// 1 million items could take minutes. Without cancellation checks:
// - User requests cancellation but nothing happens
// - Application shutdown is blocked waiting for loops to finish
// - Resources consumed long after they're needed
// - Poor user experience ("why won't it stop?!")
//
// THE PROBLEM:
// Having a CancellationToken parameter isn't enough if your loop ignores it:
//
//   public async Task ProcessItemsAsync(List<Item> items, CancellationToken ct)
//   {
//       foreach (var item in items)  // Loop runs to completion regardless!
//       {
//           Process(item);
//       }
//   }
//
// THE RULE:
// - Loops in methods with CancellationToken should check for cancellation
// - Either call ThrowIfCancellationRequested() or check IsCancellationRequested
// - Applies to: for, foreach, while, do-while
// - Code fix adds ThrowIfCancellationRequested() as first statement in loop
//
// =============================================================================

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC009: Loops should check for cancellation.
/// </summary>
public class CC009_LoopCancellation
{
    // -------------------------------------------------------------------------
    // VIOLATIONS (CC009 will warn on these)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC009 WARNING: foreach loop without cancellation check.
    /// If items has 1M elements and user cancels after 10, we still process all 1M.
    /// </summary>
    public async Task ProcessItemsAsync(
        List<string> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)  // WARNING: No cancellation check
        {
            await ProcessItemAsync(item);
        }
    }

    /// <summary>
    /// CC009 WARNING: for loop without cancellation check.
    /// This could run for hours if count is large.
    /// </summary>
    public void ProcessRangeSync(int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)  // WARNING: No cancellation check
        {
            DoHeavyWork(i);
        }
    }

    /// <summary>
    /// CC009 WARNING: while loop without cancellation check.
    /// Infinite loops without cancellation checks are especially dangerous.
    /// </summary>
    public async Task PollForUpdatesAsync(CancellationToken cancellationToken)
    {
        while (true)  // WARNING: No cancellation check - infinite loop!
        {
            await CheckForUpdatesAsync();
            await Task.Delay(1000, cancellationToken);  // Token here doesn't help the loop
        }
    }

    /// <summary>
    /// CC009 WARNING: do-while loop without cancellation check.
    /// </summary>
    public void RetryOperation(CancellationToken cancellationToken)
    {
        int attempts = 0;
        do  // WARNING: No cancellation check
        {
            attempts++;
            TryOperation();
        } while (attempts < 10 && !OperationSucceeded());
    }

    /// <summary>
    /// CC009 WARNING x2: Nested loops each need their own check.
    /// Processing a 1000x1000 grid = 1M iterations!
    /// </summary>
    public void ProcessGridAsync(int rows, int cols, CancellationToken cancellationToken)
    {
        for (int i = 0; i < rows; i++)  // WARNING 1
        {
            for (int j = 0; j < cols; j++)  // WARNING 2
            {
                ProcessCell(i, j);
            }
        }
    }

    // -------------------------------------------------------------------------
    // CORRECT PATTERNS (No warnings)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CORRECT: foreach with ThrowIfCancellationRequested().
    /// This is the recommended pattern - throws immediately when cancelled.
    /// </summary>
    public async Task ProcessItemsCorrectAsync(
        List<string> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();  // Check at start
            await ProcessItemAsync(item);
        }
    }

    /// <summary>
    /// CORRECT: for loop with ThrowIfCancellationRequested().
    /// </summary>
    public void ProcessRangeSyncCorrect(int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DoHeavyWork(i);
        }
    }

    /// <summary>
    /// CORRECT: while loop using IsCancellationRequested in condition.
    /// Good for loops that should exit gracefully rather than throw.
    /// </summary>
    public async Task PollForUpdatesCorrectAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)  // Check in condition
        {
            await CheckForUpdatesAsync();
            await Task.Delay(1000, cancellationToken);
        }
    }

    /// <summary>
    /// CORRECT: Alternative - throw in loop body for immediate exit.
    /// </summary>
    public async Task PollForUpdatesCorrectAltAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();  // Will throw
            await CheckForUpdatesAsync();
            await Task.Delay(1000, cancellationToken);
        }
    }

    /// <summary>
    /// CORRECT: do-while with cancellation check.
    /// </summary>
    public void RetryOperationCorrect(CancellationToken cancellationToken)
    {
        int attempts = 0;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            TryOperation();
        } while (attempts < 10 && !OperationSucceeded());
    }

    /// <summary>
    /// CORRECT: Nested loops with check in each.
    /// Inner loop check is most important for responsiveness.
    /// </summary>
    public void ProcessGridCorrectAsync(int rows, int cols, CancellationToken cancellationToken)
    {
        for (int i = 0; i < rows; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int j = 0; j < cols; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProcessCell(i, j);
            }
        }
    }

    /// <summary>
    /// CORRECT: Using IsCancellationRequested for graceful exit.
    /// Use this pattern when you want to clean up before exiting.
    /// </summary>
    public async Task ProcessWithCleanupAsync(
        List<string> items,
        CancellationToken cancellationToken)
    {
        var processedCount = 0;

        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"Cancelled after processing {processedCount} items");
                break;  // Exit gracefully
            }

            await ProcessItemAsync(item);
            processedCount++;
        }

        // Cleanup code runs even if cancelled
        await SaveProgressAsync(processedCount);
    }

    // -------------------------------------------------------------------------
    // LOCAL FUNCTIONS
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC009 also detects loops in local functions.
    /// </summary>
    public async Task ProcessWithLocalFunctionAsync(CancellationToken cancellationToken)
    {
        async Task ProcessBatchAsync(List<string> batch, CancellationToken ct)
        {
            foreach (var item in batch)
            {
                ct.ThrowIfCancellationRequested();  // CORRECT
                await ProcessItemAsync(item);
            }
        }

        var items = new List<string> { "a", "b", "c" };
        await ProcessBatchAsync(items, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // HELPERS
    // -------------------------------------------------------------------------

    private async Task ProcessItemAsync(string item) => await Task.Delay(10);
    private void DoHeavyWork(int i) => Thread.Sleep(10);
    private async Task CheckForUpdatesAsync() => await Task.Delay(10);
    private void TryOperation() { }
    private bool OperationSucceeded() => true;
    private void ProcessCell(int i, int j) { }
    private async Task SaveProgressAsync(int count) => await Task.CompletedTask;
}
