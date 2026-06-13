// =============================================================================
// CC010: await foreach should flow a CancellationToken
// =============================================================================
//
// WHY THIS MATTERS:
// Consuming an async stream with `await foreach` can block indefinitely waiting
// for the next element. Unless a token is threaded into the enumeration, a
// cancelled operation cannot interrupt the consumer. The framework-blessed way
// to flow the token is `source.WithCancellation(token)`, which routes it to the
// producer's [EnumeratorCancellation] parameter.
//
// THE RULE:
// - Flags `await foreach` over an IAsyncEnumerable<T> when a token is in scope
//   and the source does not already flow one.
// - Code fix wraps the source in `.WithCancellation(token)`.
// =============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC010: await foreach should flow a CancellationToken.
/// </summary>
public class CC010_AsyncEnumerable
{
    // VIOLATION (CC010 warns here)
    public async Task ConsumeBad(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        await foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    // FIXED
    public async Task ConsumeGood(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
    {
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
