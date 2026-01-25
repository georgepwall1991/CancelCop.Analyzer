// =============================================================================
// CC012: ConfigureAwait should be used (disabled by default - for library code)
// =============================================================================
//
// WHY THIS MATTERS (in library code):
// By default, await captures the current synchronization context and posts the
// continuation back to it. In library code, this can cause:
// - DEADLOCKS: When library is called from UI thread that's blocked waiting
// - PERFORMANCE ISSUES: Unnecessary context switches
// - UNEXPECTED BEHAVIOR: Library assumes it needs the original context
//
// THE RULE:
// - In LIBRARY code, use ConfigureAwait(false) to avoid capturing context
// - In APPLICATION code, usually leave it off (you WANT the UI context)
// - This rule is DISABLED by default - enable it in library projects
// - Enable via: dotnet_diagnostic.CC012.severity = warning
//
// =============================================================================

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC012: ConfigureAwait should be used.
/// NOTE: This rule is disabled by default. Enable in .editorconfig for library projects.
/// </summary>
public class CC012_ConfigureAwait
{
    // -------------------------------------------------------------------------
    // VIOLATIONS (CC012 will warn on these when enabled)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC012 INFO (when enabled): Library code should not capture context.
    /// If called from UI thread and blocked, this could deadlock.
    /// </summary>
    public async Task<int> LibraryMethodAsync()
    {
        // In library code, this captures the context unnecessarily
        await Task.Delay(1000);
        return 42;
    }

    /// <summary>
    /// CC012 INFO (when enabled): Multiple awaits without ConfigureAwait.
    /// Each await captures context - inefficient in library code.
    /// </summary>
    public async Task ProcessDataAsync(string data)
    {
        await Task.Delay(100);  // Captures context
        await Task.Delay(100);  // Captures context again
        await Task.Delay(100);  // And again...
    }

    /// <summary>
    /// CC012 INFO (when enabled): Even short operations should be configured.
    /// </summary>
    public async Task<string> TransformAsync(string input)
    {
        await Task.Yield();  // Still captures context
        return input.ToUpper();
    }

    // -------------------------------------------------------------------------
    // CORRECT PATTERNS FOR LIBRARY CODE (No warnings)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CORRECT: Library code with ConfigureAwait(false).
    /// No context is captured - safe to call from anywhere.
    /// </summary>
    public async Task<int> LibraryMethodCorrectAsync()
    {
        await Task.Delay(1000).ConfigureAwait(false);
        return 42;
    }

    /// <summary>
    /// CORRECT: All awaits configured in library code.
    /// </summary>
    public async Task ProcessDataCorrectAsync(string data)
    {
        await Task.Delay(100).ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);
    }

    /// <summary>
    /// CORRECT: ConfigureAwait(true) explicitly captures context.
    /// Use when you NEED the context (rare in library code).
    /// </summary>
    public async Task NeedsContextAsync()
    {
        await Task.Delay(100).ConfigureAwait(true);
    }

    // -------------------------------------------------------------------------
    // APPLICATION CODE PATTERNS (CC012 should be disabled)
    // -------------------------------------------------------------------------

    /// <summary>
    /// IN APPLICATION CODE: It's fine to NOT use ConfigureAwait.
    /// Application code often needs to update UI after async operations.
    /// Keep CC012 disabled in application projects.
    /// </summary>
    public async Task ApplicationCodeAsync()
    {
        // In WPF/WinForms/etc., you WANT to return to UI thread
        await Task.Delay(1000);
        // UpdateUI(); // This needs to run on UI thread
    }

    /// <summary>
    /// IN ASP.NET CORE: ConfigureAwait(false) is not necessary.
    /// ASP.NET Core has no SynchronizationContext, so there's no deadlock risk.
    /// Still, it doesn't hurt in library code.
    /// </summary>
    public async Task AspNetCoreCodeAsync()
    {
        // No SynchronizationContext in ASP.NET Core
        await Task.Delay(1000);
    }

    // -------------------------------------------------------------------------
    // CONFIGURATION EXAMPLES
    // -------------------------------------------------------------------------

    /// <summary>
    /// To enable CC012 in your library project, add to .editorconfig:
    ///
    /// [*.cs]
    /// dotnet_diagnostic.CC012.severity = warning
    ///
    /// Or to make it an error:
    /// dotnet_diagnostic.CC012.severity = error
    /// </summary>
    public static void ConfigurationExample()
    {
        // This is just documentation - see .editorconfig
    }
}
