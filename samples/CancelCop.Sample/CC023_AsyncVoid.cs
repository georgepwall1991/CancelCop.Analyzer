// =============================================================================
// CC023: Avoid async void (non-event-handler)
// =============================================================================
//
// WHY THIS MATTERS:
// An async void method cannot be awaited, so a caller cannot observe completion,
// flow cancellation into it, or catch its exceptions — an unhandled exception in
// one crashes the process. Return Task instead. Event handlers are excluded.
//
// THE RULE:
// - Flags an async void method whose signature is not the event-handler shape.
// - Code fix changes the return type to Task.
// =============================================================================

using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC023: avoid async void.
/// </summary>
public class CC023_AsyncVoid
{
    private Task WorkAsync() => Task.CompletedTask;

    // VIOLATION (CC023 warns here)
    public async void ProcessBad() => await WorkAsync();

    // FIXED
    public async Task ProcessGood() => await WorkAsync();
}
