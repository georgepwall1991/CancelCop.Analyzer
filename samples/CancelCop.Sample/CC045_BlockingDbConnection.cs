// =============================================================================
// CC045: Avoid blocking DbConnection.Open in async code
// =============================================================================
//
// WHY THIS MATTERS:
// DbConnection.Open parks a thread-pool thread on a database handshake.
// That wait is not a CancellationToken. OpenAsync yields the thread and
// has accepted a token since .NET Framework 4.5. Concrete providers match
// through the override chain.
//
// WHY THIS IS NOT CC003:
// CC003 is EF Core query methods. ADO.NET Open is a separate type.
// =============================================================================

using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC045: blocking DbConnection.Open in async code.
/// </summary>
public class CC045_BlockingDbConnection
{
    // VIOLATION (CC045 warns here) — parks a pooled thread on a handshake
    public async Task OpenBad(DbConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connection.Open();
        await Task.Yield();
    }

    // FIXED (CC045 fixer) — yields the thread; OpenAsync has taken a token since .NET 4.5
    public async Task OpenGood(DbConnection connection, CancellationToken cancellationToken)
    {
        await connection.OpenAsync(cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void OpenSync(DbConnection connection)
    {
        connection.Open();
    }
}
