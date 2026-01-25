// =============================================================================
// CC007: Avoid CancellationToken.None when a token is available
// =============================================================================
//
// WHY THIS MATTERS:
// Using CancellationToken.None explicitly ignores cancellation, even when a token
// is available. This defeats the purpose of accepting a CancellationToken parameter
// and leads to:
// - Operations that cannot be cancelled despite accepting a token
// - Misleading API - callers think cancellation works but it doesn't
// - Wasted resources on abandoned requests
//
// THE RULE:
// - When a CancellationToken is in scope, use it instead of CancellationToken.None
// - Also detects default(CancellationToken) with same behavior
// - Code fix replaces None with the available token
//
// =============================================================================

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC007: Avoid CancellationToken.None when a token is available.
/// </summary>
public class CC007_CancellationTokenNone
{
    // -------------------------------------------------------------------------
    // VIOLATIONS (CC007 will warn on these)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC007 WARNING: Using CancellationToken.None when 'ct' is available.
    /// The Task.Delay cannot be cancelled even though the method accepts a token!
    /// </summary>
    public async Task ProcessWithIgnoredTokenAsync(CancellationToken ct)
    {
        // BAD: Explicitly ignoring the available token
        await Task.Delay(5000, CancellationToken.None);
    }

    /// <summary>
    /// CC007 WARNING: default(CancellationToken) is equivalent to None.
    /// Same problem - the operation cannot be cancelled.
    /// </summary>
    public async Task ProcessWithDefaultTokenAsync(CancellationToken cancellationToken)
    {
        // BAD: default(CancellationToken) is the same as None
        await Task.Delay(5000, default(CancellationToken));
    }

    /// <summary>
    /// CC007 WARNING: Even in helper methods, don't ignore tokens.
    /// </summary>
    public async Task<int> ComputeAsync(int value, CancellationToken ct)
    {
        // BAD: Long computation ignoring the token
        await Task.Delay(10000, CancellationToken.None);
        return value * 2;
    }

    // -------------------------------------------------------------------------
    // CORRECT PATTERNS (No warnings)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CORRECT: Using the provided CancellationToken.
    /// Now callers can actually cancel the operation.
    /// </summary>
    public async Task ProcessWithTokenAsync(CancellationToken ct)
    {
        await Task.Delay(5000, ct);
    }

    /// <summary>
    /// CORRECT: Pass token to all async operations.
    /// </summary>
    public async Task<int> ComputeCorrectlyAsync(int value, CancellationToken ct)
    {
        await Task.Delay(1000, ct);
        return value * 2;
    }

    /// <summary>
    /// CORRECT: CancellationToken.None is fine when no token is available.
    /// This method doesn't accept a token, so None is appropriate.
    /// </summary>
    public async Task ProcessWithoutTokenAsync()
    {
        // OK: No token available in scope
        await Task.Delay(1000, CancellationToken.None);
    }

    /// <summary>
    /// CORRECT: In local functions, use the local token if available.
    /// </summary>
    public async Task ProcessWithLocalFunctionAsync(CancellationToken ct)
    {
        async Task LocalAsync(CancellationToken localToken)
        {
            // Good: Using the local function's token
            await Task.Delay(1000, localToken);
        }

        await LocalAsync(ct);
    }
}
