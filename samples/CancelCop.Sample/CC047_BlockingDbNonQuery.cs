// =============================================================================
// CC047: Avoid blocking DbCommand.ExecuteNonQuery in async code
// =============================================================================
//
// WHY THIS MATTERS:
// DbCommand.ExecuteNonQuery parks a thread-pool thread on a command that
// does not return rows. That wait is not a CancellationToken.
// ExecuteNonQueryAsync yields the thread and has accepted a token since
// .NET Framework 4.5.
//
// WHY THIS IS NOT CC003 / CC045 / CC046:
// CC003 is EF Core. CC045 is DbConnection.Open. CC046 is ExecuteReader.
// ExecuteScalar is CC048.
// =============================================================================

using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC047: blocking DbCommand.ExecuteNonQuery in async code.
/// </summary>
public class CC047_BlockingDbNonQuery
{
    // VIOLATION (CC047 warns here) — parks a pooled thread on a command
    public async Task ExecBad(DbCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        command.ExecuteNonQuery();
        await Task.Yield();
    }

    // FIXED — yields the thread; ExecuteNonQueryAsync has taken a token since .NET 4.5
    public async Task ExecGood(DbCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void ExecSync(DbCommand command)
    {
        command.ExecuteNonQuery();
    }
}
