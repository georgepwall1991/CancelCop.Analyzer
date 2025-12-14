// =============================================================================
// CC002: CancellationToken must be propagated to async calls
// =============================================================================
//
// WHY THIS MATTERS:
// Having a CancellationToken parameter is useless if you don't pass it along!
// When a method accepts a token but doesn't propagate it to inner async calls:
// - Cancellation requests are silently ignored
// - The operation continues even after caller requested cancellation
// - Resources continue to be consumed unnecessarily
// - Graceful shutdown becomes impossible
//
// THE RULE:
// - When calling async methods that accept CancellationToken, pass your token
// - Applies to: Task.Delay, Task.Run, and any custom async method with token parameter
// - The analyzer detects when you have a token available but don't use it
//
// =============================================================================

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC002: CancellationToken must be propagated to async calls.
/// </summary>
public class CC002_TokenPropagation
{
    // -------------------------------------------------------------------------
    // VIOLATIONS (CC002 will warn on these)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC002 WARNING on Task.Delay: Token is available but not passed.
    /// If caller cancels, this delay will still complete fully.
    /// </summary>
    public async Task DelayWithoutPropagationAsync(CancellationToken cancellationToken)
    {
        // BAD: Task.Delay has an overload that accepts CancellationToken
        await Task.Delay(5000); // Will wait full 5 seconds even if cancelled
        Console.WriteLine("Delay completed");
    }

    /// <summary>
    /// CC002 WARNING on Task.Run: Background work won't be cancelled.
    /// </summary>
    public async Task RunWithoutPropagationAsync(CancellationToken cancellationToken)
    {
        // BAD: Task.Run can accept a CancellationToken
        await Task.Run(() => HeavyComputation());
    }

    /// <summary>
    /// CC002 WARNING: Custom async method has token parameter but we don't pass it.
    /// </summary>
    public async Task ProcessWithoutPropagationAsync(CancellationToken cancellationToken)
    {
        // BAD: HelperAsync accepts a token but we're not passing ours
        await HelperAsync();
    }

    /// <summary>
    /// CC002 WARNING (multiple): Multiple calls missing token propagation.
    /// Each async call that supports cancellation should receive the token.
    /// </summary>
    public async Task MultipleViolationsAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100);     // WARNING 1
        await HelperAsync();        // WARNING 2
        await Task.Delay(200);     // WARNING 3
    }

    // -------------------------------------------------------------------------
    // CORRECT PATTERNS (No warnings)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CORRECT: Token is propagated to Task.Delay.
    /// Cancellation will immediately stop the delay.
    /// </summary>
    public async Task DelayWithPropagationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(5000, cancellationToken); // Cancels immediately when requested
        Console.WriteLine("Delay completed");
    }

    /// <summary>
    /// CORRECT: Token is propagated to Task.Run.
    /// </summary>
    public async Task RunWithPropagationAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() => HeavyComputation(), cancellationToken);
    }

    /// <summary>
    /// CORRECT: Token is passed to helper method.
    /// </summary>
    public async Task ProcessWithPropagationAsync(CancellationToken cancellationToken)
    {
        await HelperAsync(cancellationToken);
    }

    /// <summary>
    /// CORRECT: All async calls receive the token.
    /// </summary>
    public async Task AllCallsPropagatedAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        await HelperAsync(cancellationToken);
        await Task.Delay(200, cancellationToken);
    }

    /// <summary>
    /// CORRECT: No token parameter means no propagation required.
    /// (But CC001 would warn that this public method needs a token!)
    /// </summary>
    public async Task NoTokenToPropagate()
    {
        await Task.Delay(100); // OK - we don't have a token to propagate
    }

    // -------------------------------------------------------------------------
    // LOCAL FUNCTIONS (v1.2.0+)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC002 also works with local functions (added in v1.2.0).
    /// </summary>
    public async Task LocalFunctionExampleAsync(CancellationToken cancellationToken)
    {
        // Local function with its own token parameter
        async Task ProcessItemAsync(int item, CancellationToken ct)
        {
            // This would warn if we didn't pass ct:
            await Task.Delay(100, ct); // CORRECT: uses local function's token
        }

        await ProcessItemAsync(1, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // HELPERS
    // -------------------------------------------------------------------------

    private async Task HelperAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);
    }

    private void HeavyComputation()
    {
        // Simulates CPU-bound work
        Thread.Sleep(100);
    }
}
