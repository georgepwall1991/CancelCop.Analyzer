// =============================================================================
// CC006: CancellationToken should be the last parameter (convention)
// =============================================================================
//
// WHY THIS MATTERS:
// This is a style convention that improves code consistency and readability.
// The .NET Framework Design Guidelines recommend placing CancellationToken last.
//
// Benefits of consistent parameter ordering:
// - Easier to read and understand method signatures
// - Consistent with BCL (Base Class Library) conventions
// - Optional default values work better at the end
// - Easier to add new parameters without breaking callers
//
// THE RULE:
// - CancellationToken should be the last (or second-to-last) parameter
// - Severity: Info (not a warning, just a style suggestion)
// - No code fix yet (parameter reordering affects all call sites)
//
// =============================================================================

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC006: CancellationToken should be the last parameter.
/// </summary>
public class CC006_ParameterPosition
{
    // -------------------------------------------------------------------------
    // VIOLATIONS (CC006 will report Info diagnostic)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC006 INFO: CancellationToken is first, but should be last.
    /// This is inconsistent with .NET conventions.
    /// </summary>
    public async Task ProcessAsync(
        CancellationToken cancellationToken,  // Should be last
        string data)
    {
        await Task.Delay(100, cancellationToken);
        Console.WriteLine($"Processed: {data}");
    }

    /// <summary>
    /// CC006 INFO: CancellationToken is in the middle.
    /// Parameters after it look awkward.
    /// </summary>
    public async Task FetchDataAsync(
        int id,
        CancellationToken cancellationToken,  // Should be last
        bool includeDetails,
        string format)
    {
        await Task.Delay(100, cancellationToken);
    }

    /// <summary>
    /// CC006 INFO: Even with just two parameters, token should be last.
    /// </summary>
    public async Task<string> GetValueAsync(
        CancellationToken cancellationToken,  // Should be last
        string key)
    {
        await Task.Delay(50, cancellationToken);
        return $"Value for {key}";
    }

    // -------------------------------------------------------------------------
    // CORRECT PATTERNS (No diagnostics)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CORRECT: CancellationToken is the last parameter.
    /// This follows .NET conventions.
    /// </summary>
    public async Task ProcessCorrectAsync(
        string data,
        CancellationToken cancellationToken)  // Last - correct!
    {
        await Task.Delay(100, cancellationToken);
        Console.WriteLine($"Processed: {data}");
    }

    /// <summary>
    /// CORRECT: CancellationToken at the end with multiple parameters.
    /// </summary>
    public async Task FetchDataCorrectAsync(
        int id,
        bool includeDetails,
        string format,
        CancellationToken cancellationToken)  // Last - correct!
    {
        await Task.Delay(100, cancellationToken);
    }

    /// <summary>
    /// CORRECT: CancellationToken last, with default value.
    /// Default values work best on trailing parameters.
    /// </summary>
    public async Task<string> GetValueCorrectAsync(
        string key,
        CancellationToken cancellationToken = default)  // Last with default
    {
        await Task.Delay(50, cancellationToken);
        return $"Value for {key}";
    }

    /// <summary>
    /// CORRECT: Only CancellationToken parameter - position doesn't matter.
    /// </summary>
    public async Task SimpleOperationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // WHY THIS CONVENTION EXISTS
    // -------------------------------------------------------------------------
    //
    // Example from the .NET BCL:
    //
    //   Task.Delay(int millisecondsDelay, CancellationToken cancellationToken)
    //   File.ReadAllTextAsync(string path, CancellationToken cancellationToken)
    //   HttpClient.GetAsync(string requestUri, CancellationToken cancellationToken)
    //
    // All BCL methods follow this pattern. Your code should too!
    //
    // Another benefit: optional parameters must come after required ones.
    // CancellationToken often has a default value of `default`, so it should
    // be at the end anyway.
}
