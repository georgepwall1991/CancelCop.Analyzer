// =============================================================================
// CC034: ParallelOptions should set CancellationToken
// =============================================================================
//
// WHY THIS MATTERS:
// ParallelOptions.CancellationToken is the ONLY way to cancel a Parallel loop.
// Without it the loop runs every partition to completion no matter what the
// caller wants — and a long parallel loop over a large collection is precisely
// the work most worth stopping.
//
// THE GAP THIS CLOSES:
// CC002 fires on a CALL that has a token-accepting overload. Here the token is
// neither an argument nor an overload: it is a property set in an object
// initializer, and Parallel.ForEach has no token-taking overload at all. There is
// nothing for CC002 to match on, so this omission is invisible to it.
//
// THE RULE:
// - Fires only when a token is actually in scope (same walk as CC002/CC012) —
//   with nothing to suggest, the rule stays quiet.
// - Quiet when the token is assigned afterwards
//   (options.CancellationToken = token), which is equally correct and common when
//   the options are built up conditionally.
// =============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC034: ParallelOptions created without a CancellationToken.
/// </summary>
public class CC034_ParallelOptionsToken
{
    // VIOLATION (CC034 warns here) — nothing can stop this loop
    public void ProcessBad(int[] items, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        Parallel.ForEach(items, options, Handle);
    }

    // FIXED — the loop observes cancellation between partitions
    public void ProcessGood(int[] items, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken,
        };
        Parallel.ForEach(items, options, Handle);
    }

    // CLEAN — assigning the property afterwards is equally correct
    public void ProcessConditional(int[] items, bool limit, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions();
        if (limit)
        {
            options.MaxDegreeOfParallelism = 4;
        }

        options.CancellationToken = cancellationToken;
        Parallel.ForEach(items, options, Handle);
    }

    // CLEAN — no token in scope, so there is nothing to suggest
    public void ProcessWithoutToken(int[] items)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        Parallel.ForEach(items, options, Handle);
    }

    private static void Handle(int item) { }
}
