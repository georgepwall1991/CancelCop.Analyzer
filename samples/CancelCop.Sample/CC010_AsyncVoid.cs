// =============================================================================
// CC010: Avoid async void methods
// =============================================================================
//
// WHY THIS MATTERS:
// Async void methods are "fire and forget" - they have no way to report completion
// or failure back to the caller. This is dangerous because:
// - Exceptions thrown in async void methods cannot be caught by the caller
// - Unhandled exceptions in async void crash the entire application
// - There's no way to wait for the operation to complete
// - It's impossible to know if the operation succeeded or failed
//
// THE RULE:
// - Use async Task instead of async void
// - The only valid use of async void is for event handlers
// - The analyzer ignores methods with EventArgs-based signatures
// - Code fix changes void to Task
//
// =============================================================================

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC010: Avoid async void methods.
/// </summary>
public class CC010_AsyncVoid
{
    // -------------------------------------------------------------------------
    // VIOLATIONS (CC010 will warn on these)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC010 WARNING: Async void method cannot have exceptions caught.
    /// If Task.Delay throws, the application crashes!
    /// </summary>
    public async void ProcessAsync()
    {
        await Task.Delay(1000);
        throw new InvalidOperationException("This will crash the app!");
    }

    /// <summary>
    /// CC010 WARNING: Even private async void is dangerous.
    /// </summary>
    private async void InternalProcessAsync()
    {
        await Task.Delay(500);
    }

    /// <summary>
    /// CC010 WARNING: Caller has no way to await completion.
    /// </summary>
    public async void StartBackgroundWork()
    {
        await Task.Delay(10000);  // 10 seconds
        Console.WriteLine("Done!"); // Caller never knows when this happens
    }

    /// <summary>
    /// Demonstrates a local async void function (also warned).
    /// </summary>
    public void UseLocalAsyncVoid()
    {
        // CC010 WARNING: Local async void function
        async void LocalAsync()
        {
            await Task.Delay(1000);
        }

        LocalAsync(); // Fire and forget - dangerous!
    }

    // -------------------------------------------------------------------------
    // CORRECT PATTERNS (No warnings)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CORRECT: Using async Task allows proper exception handling.
    /// </summary>
    public async Task ProcessCorrectlyAsync()
    {
        await Task.Delay(1000);
        // Exceptions here can be caught by the caller
    }

    /// <summary>
    /// CORRECT: Caller can await completion.
    /// </summary>
    public async Task StartBackgroundWorkAsync()
    {
        await Task.Delay(10000);
        Console.WriteLine("Done!");
    }

    /// <summary>
    /// CORRECT: Event handler can be async void.
    /// This is the ONE valid use case for async void.
    /// </summary>
    public async void OnButtonClick(object sender, EventArgs e)
    {
        // This is OK - event handlers are designed for async void
        await Task.Delay(100);
        Console.WriteLine("Button was clicked");
    }

    /// <summary>
    /// CORRECT: Custom event args also allowed.
    /// </summary>
    public async void OnCustomEvent(object sender, CustomEventArgs e)
    {
        await Task.Delay(100);
    }

    /// <summary>
    /// CORRECT: Local async Task function.
    /// </summary>
    public async Task UseLocalAsyncTaskAsync()
    {
        async Task LocalAsync()
        {
            await Task.Delay(1000);
        }

        await LocalAsync(); // Can be awaited properly
    }

    /// <summary>
    /// Example of proper exception handling with async Task.
    /// </summary>
    public async Task DemonstrateExceptionHandlingAsync()
    {
        try
        {
            await ProcessCorrectlyAsync();
        }
        catch (Exception ex)
        {
            // Exceptions can be caught with async Task!
            Console.WriteLine($"Handled exception: {ex.Message}");
        }
    }
}

/// <summary>
/// Custom EventArgs for demonstration.
/// </summary>
public class CustomEventArgs : EventArgs
{
    public string Message { get; set; } = string.Empty;
}
