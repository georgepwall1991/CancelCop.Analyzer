// =============================================================================
// CC001: Public async methods must have CancellationToken parameter
// =============================================================================
//
// WHY THIS MATTERS:
// Public async methods are entry points that callers use. Without a CancellationToken
// parameter, callers cannot cancel long-running operations, leading to:
// - Wasted resources on abandoned requests
// - Poor user experience (can't cancel)
// - Potential memory leaks from orphaned tasks
// - Blocked graceful shutdown in web applications
//
// THE RULE:
// - Public and protected async methods returning Task, Task<T>, ValueTask, or ValueTask<T>
//   should accept a CancellationToken parameter
// - Private methods are excluded (internal implementation details)
// - The analyzer provides a code fix to add the parameter automatically
//
// =============================================================================

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC001: Public async methods must have CancellationToken parameter.
/// </summary>
public class CC001_PublicAsyncMethods
{
    // -------------------------------------------------------------------------
    // VIOLATIONS (CC001 will warn on these)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC001 WARNING: This public async method has no way for callers to cancel it.
    /// If this operation takes 30 seconds, the caller is stuck waiting.
    /// </summary>
    public async Task FetchDataAsync()
    {
        await Task.Delay(1000); // Simulates long operation
        Console.WriteLine("Data fetched (no cancellation possible)");
    }

    /// <summary>
    /// CC001 WARNING: Protected methods are also checked because derived classes
    /// may need to cancel operations.
    /// </summary>
    protected async Task ProcessInternalAsync()
    {
        await Task.Delay(500);
    }

    /// <summary>
    /// CC001 WARNING: ValueTask methods are also checked (added in v1.2.0).
    /// ValueTask is commonly used for high-performance scenarios.
    /// </summary>
    public async ValueTask<int> ComputeValueAsync()
    {
        await Task.Delay(100);
        return 42;
    }

    // -------------------------------------------------------------------------
    // CORRECT PATTERNS (No warnings)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CORRECT: Public async method with CancellationToken parameter.
    /// Callers can cancel this operation at any time.
    /// </summary>
    public async Task FetchDataAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1000, cancellationToken);
        Console.WriteLine("Data fetched (cancellation supported)");
    }

    /// <summary>
    /// CORRECT: Protected method with cancellation support.
    /// </summary>
    protected async Task ProcessInternalAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(500, cancellationToken);
    }

    /// <summary>
    /// CORRECT: ValueTask with cancellation support.
    /// </summary>
    public async ValueTask<int> ComputeValueAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        return 42;
    }

    /// <summary>
    /// CORRECT: Private methods don't need CancellationToken.
    /// They are implementation details, not public API.
    /// The calling public method should handle cancellation.
    /// </summary>
    private async Task InternalHelperAsync()
    {
        await Task.Delay(50);
    }

    /// <summary>
    /// CORRECT: Default parameter value allows backward compatibility.
    /// Existing callers don't need to change, but new callers can pass tokens.
    /// </summary>
    public async Task BackwardCompatibleAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
    }
}
