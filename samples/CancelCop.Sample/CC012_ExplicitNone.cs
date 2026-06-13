// =============================================================================
// CC012: Avoid passing CancellationToken.None when a token is in scope
// =============================================================================
//
// WHY THIS MATTERS:
// Passing CancellationToken.None (or default) to a call explicitly opts that
// call out of cancellation. When the surrounding method already has a token to
// offer, this is usually an oversight: the operation can no longer be cancelled.
// Reported as Info because it is occasionally intentional (best-effort cleanup).
//
// THE RULE:
// - Flags an explicit CancellationToken.None / default argument bound to a
//   CancellationToken when an in-scope token is available.
// - Code fix replaces it with the in-scope token.
// =============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC012: avoid passing CancellationToken.None when a token exists.
/// </summary>
public class CC012_ExplicitNone
{
    private Task DoAsync(CancellationToken token) => Task.CompletedTask;

    // VIOLATION (CC012 suggests here)
    public async Task RunBad(CancellationToken cancellationToken)
        => await DoAsync(CancellationToken.None);

    // FIXED
    public async Task RunGood(CancellationToken cancellationToken)
        => await DoAsync(cancellationToken);
}
