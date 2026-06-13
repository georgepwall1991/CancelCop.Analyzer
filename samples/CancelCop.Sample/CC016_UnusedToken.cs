// =============================================================================
// CC016: CancellationToken parameter is accepted but never used
// =============================================================================
//
// WHY THIS MATTERS:
// Accepting a CancellationToken advertises that the operation honours
// cancellation. A body that never touches the token silently breaks that
// promise: callers pass a token expecting it to take effect, but nothing
// observes it. Reported as Info because a token is occasionally reserved.
//
// THE RULE:
// - Flags a method/local function that does async work (has an await) but never
//   references its CancellationToken parameter.
// - Excludes overrides / interface implementations (they cannot drop the param).
// - Analyzer-only (wiring up or removing a parameter is too invasive to automate).
// =============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC016: an accepted-but-unused CancellationToken parameter.
/// </summary>
public class CC016_UnusedToken
{
    private Task WorkAsync() => Task.CompletedTask;

    // VIOLATION (CC016 suggests here)
    public async Task SaveBad(string text, CancellationToken cancellationToken)
    {
        await WorkAsync();
    }

    // FIXED
    public async Task SaveGood(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WorkAsync();
    }
}
