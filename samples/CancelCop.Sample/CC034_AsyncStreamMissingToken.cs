// =============================================================================
// CC034: Async iterator should have a CancellationToken parameter
// =============================================================================
//
// WHY THIS MATTERS:
// An async stream is long-lived by nature — the consumer pulls items one at a
// time and the producer stays suspended in between. Without a token there is no
// way to stop it: a consumer that abandons the enumeration leaves the producer's
// pending work with nothing to cancel, and .WithCancellation(token) at the call
// site has nothing to flow into.
//
// THE GAP THIS CLOSES:
// - CC001 only covers methods returning Task/ValueTask, so it never sees an
//   iterator.
// - CC011 requires [EnumeratorCancellation], but only once a token parameter
//   exists.
// - CC010 flags the consumer for not calling .WithCancellation.
// A stream declared with no token at all slips past all three. CC034 is the
// producer-side entry point that makes the others reachable.
//
// THE RULE:
// - Only public/protected ITERATORS (a method that yields). A method that merely
//   returns an IAsyncEnumerable is a pass-through, not the producer.
// - Signatures fixed by something else are excluded — overrides, interface
//   implementations, extern — because adding a parameter breaks the contract.
// - The fix adds [EnumeratorCancellation] too; a bare token would be silently
//   ignored by .WithCancellation, which is exactly what CC011 exists to say.
// =============================================================================

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC034: an async iterator with no CancellationToken parameter.
/// </summary>
public class CC034_AsyncStreamMissingToken
{
    // VIOLATION (CC034 warns here) — nothing can stop this enumeration
    public async IAsyncEnumerable<int> ReadBad()
    {
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(100);
            yield return i;
        }
    }

    // FIXED — the token flows in, and [EnumeratorCancellation] makes
    // .WithCancellation(token) at the call site actually reach it
    public async IAsyncEnumerable<int> ReadGood(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(100, cancellationToken);
            yield return i;
        }
    }

    // CLEAN — a pass-through does not produce the items, so its signature is not
    // what stops the enumeration
    public IAsyncEnumerable<int> Forward() => ReadGood();

    // CLEAN — private: every caller is in view, so the omission is a local decision
    private async IAsyncEnumerable<int> ReadInternal()
    {
        await Task.Yield();
        yield return 1;
    }
}
