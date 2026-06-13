// =============================================================================
// CC014: CancellationTokenSource should be disposed
// =============================================================================
//
// WHY THIS MATTERS:
// CancellationTokenSource is IDisposable — it can own a Timer and a WaitHandle.
// A source created in a method and used only locally must be disposed, or those
// resources leak until finalization. The cleanest fix is a `using` declaration.
//
// THE RULE:
// - Flags a local CancellationTokenSource that is never disposed and never
//   escapes (not returned, assigned out, passed as an argument, or captured).
// - Code fix converts the declaration into a `using` declaration.
// =============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC014: CancellationTokenSource should be disposed.
/// </summary>
public class CC014_UndisposedTokenSource
{
    // VIOLATION (CC014 warns here)
    public async Task RunBad(CancellationToken ct)
    {
        var cts = new CancellationTokenSource();
        await Task.Delay(1000, cts.Token);
    }

    // FIXED
    public async Task RunGood(CancellationToken ct)
    {
        using var cts = new CancellationTokenSource();
        await Task.Delay(1000, cts.Token);
    }
}
