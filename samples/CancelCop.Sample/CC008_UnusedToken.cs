// =============================================================================
// CC008: CancellationToken parameter is not used
// =============================================================================
//
// WHY THIS MATTERS:
// Accepting a CancellationToken parameter creates an expectation that the method
// supports cancellation. If the token is never used:
// - Callers are misled about the method's behavior
// - Cancellation requests are silently ignored
// - Users can't cancel long-running operations
// - Resources are wasted on abandoned operations
//
// THE RULE:
// - CancellationToken parameters should be used (passed to async calls or checked)
// - The analyzer detects tokens that are never referenced in the method body
// - Valid uses include: passing to other methods, ThrowIfCancellationRequested(),
//   or checking IsCancellationRequested
//
// =============================================================================

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC008: CancellationToken parameter is not used.
/// </summary>
public class CC008_UnusedToken
{
    // -------------------------------------------------------------------------
    // VIOLATIONS (CC008 will warn on these)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC008 WARNING: The 'ct' parameter is accepted but never used.
    /// The 5-second delay cannot be cancelled!
    /// </summary>
    public async Task ProcessAsync(CancellationToken ct)
    {
        // BAD: Token is ignored completely
        await Task.Delay(5000);
        Console.WriteLine("Done");
    }

    /// <summary>
    /// CC008 WARNING: Token not used in expression body.
    /// </summary>
    public Task DelayAsync(CancellationToken ct) => Task.Delay(5000);

    /// <summary>
    /// CC008 WARNING: Token exists but is never referenced.
    /// </summary>
    public async Task<int> ComputeAsync(int value, CancellationToken cancellationToken)
    {
        // Multiple async operations, none using the token
        await Task.Delay(1000);
        await Task.Delay(1000);
        return value * 2;
    }

    // -------------------------------------------------------------------------
    // CORRECT PATTERNS (No warnings)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CORRECT: Token is passed to Task.Delay.
    /// </summary>
    public async Task ProcessWithTokenAsync(CancellationToken ct)
    {
        await Task.Delay(5000, ct);
        Console.WriteLine("Done");
    }

    /// <summary>
    /// CORRECT: Token is checked with ThrowIfCancellationRequested.
    /// </summary>
    public async Task ProcessWithCheckAsync(CancellationToken ct)
    {
        for (int i = 0; i < 100; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// CORRECT: Token's IsCancellationRequested is checked.
    /// </summary>
    public async Task ProcessWithPollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(100);
            Console.WriteLine("Working...");
        }
    }

    /// <summary>
    /// CORRECT: Token is passed to a linked token source.
    /// </summary>
    public async Task ProcessWithLinkedSourceAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30)); // Add timeout
        await Task.Delay(1000, cts.Token);
    }

    /// <summary>
    /// CORRECT: Token passed to all async operations.
    /// </summary>
    public async Task<int> ComputeCorrectlyAsync(int value, CancellationToken ct)
    {
        await Task.Delay(1000, ct);
        ct.ThrowIfCancellationRequested();
        await Task.Delay(1000, ct);
        return value * 2;
    }
}
