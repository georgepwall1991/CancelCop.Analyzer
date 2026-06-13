// =============================================================================
// CC011: Async-iterator CancellationToken should be [EnumeratorCancellation]
// =============================================================================
//
// WHY THIS MATTERS:
// A consumer flows a token into an async stream with
// `source.WithCancellation(token)`. That token is delivered only to the iterator
// parameter marked [EnumeratorCancellation]. Without the attribute the parameter
// silently receives `default` and cancellation never reaches the producer.
// This is the producer-side complement to CC010.
//
// THE RULE:
// - Flags an `async IAsyncEnumerable<T>` iterator whose CancellationToken
//   parameter is not marked [EnumeratorCancellation].
// - Code fix adds the attribute and the System.Runtime.CompilerServices import.
// =============================================================================

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC011: async-iterator token should be [EnumeratorCancellation].
/// </summary>
public class CC011_EnumeratorCancellation
{
    // VIOLATION (CC011 warns here)
    public async IAsyncEnumerable<int> ReadBad(CancellationToken token)
    {
        yield return 1;
        await Task.CompletedTask;
    }

    // FIXED
    public async IAsyncEnumerable<int> ReadGood([EnumeratorCancellation] CancellationToken token)
    {
        yield return 1;
        await Task.CompletedTask;
    }
}
