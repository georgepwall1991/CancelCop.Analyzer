// =============================================================================
// CC046: Avoid blocking DbCommand.ExecuteReader in async code
// =============================================================================
//
// WHY THIS MATTERS:
// DbCommand.ExecuteReader parks a thread-pool thread on a database query.
// That wait is not a CancellationToken. ExecuteReaderAsync yields the thread
// and has accepted a token since .NET Framework 4.5. Concrete providers
// hide ExecuteReader with `new` for a covariant reader and still match.
//
// WHY THIS IS NOT CC003 OR CC045:
// CC003 is EF Core query methods. CC045 is DbConnection.Open.
// ExecuteNonQuery is CC047. ExecuteScalar is CC048.
// =============================================================================

using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC046: blocking DbCommand.ExecuteReader in async code.
/// </summary>
public class CC046_BlockingDbCommand
{
    // VIOLATION (CC046 warns here) — parks a pooled thread on a query
    public async Task ReadBad(DbCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteReader();
        await Task.Yield();
    }

    // FIXED — yields the thread; ExecuteReaderAsync has taken a token since .NET 4.5
    public async Task ReadGood(DbCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteReaderAsync(cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void ReadSync(DbCommand command)
    {
        command.ExecuteReader();
    }
}
