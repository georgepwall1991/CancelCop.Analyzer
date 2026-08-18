// =============================================================================
// CC048: Avoid blocking DbCommand.ExecuteScalar in async code
// =============================================================================
//
// WHY THIS MATTERS:
// DbCommand.ExecuteScalar parks a thread-pool thread on a single-value
// query. That wait is not a CancellationToken. ExecuteScalarAsync yields
// the thread and has accepted a token since .NET Framework 4.5.
//
// WHY THIS IS NOT CC003 / CC045 / CC046 / CC047:
// CC003 is EF Core. CC045 is Open. CC046 is ExecuteReader. CC047 is
// ExecuteNonQuery.
// =============================================================================

using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC048: blocking DbCommand.ExecuteScalar in async code.
/// </summary>
public class CC048_BlockingDbScalar
{
    // VIOLATION (CC048 warns here) — parks a pooled thread on a scalar query
    public async Task ScalarBad(DbCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteScalar();
        await Task.Yield();
    }

    // FIXED — yields the thread; ExecuteScalarAsync has taken a token since .NET 4.5
    public async Task ScalarGood(DbCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteScalarAsync(cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void ScalarSync(DbCommand command)
    {
        command.ExecuteScalar();
    }
}
