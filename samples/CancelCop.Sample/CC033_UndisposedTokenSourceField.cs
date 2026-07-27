// =============================================================================
// CC033: CancellationTokenSource field is never disposed
// =============================================================================
//
// WHY THIS MATTERS:
// A CancellationTokenSource owns a timer (once a delay is set) and a registration
// list that every linked token and every Register callback adds to. A field keeps
// that alive for the whole lifetime of the owning object, so a source that is
// never disposed leaks for as long as its owner does. Linked sources are worse:
// an undisposed child stays attached to its parent's callback list, so a
// long-lived parent accumulates every child ever created.
//
// HOW THIS DIFFERS FROM CC014:
// CC014 covers a LOCAL source, where the fix is mechanical — make it a `using`
// declaration. A field's lifetime is the object's, so the resolution is to
// implement IDisposable (or dispose it in the existing one) and let the owner's
// disposal cascade. That is a design change, so CC033 is analyzer-only.
//
// THE RULE:
// - Fires only when the declaring type CREATES the source. An injected source is
//   owned by whoever created it, and disposing it would be a bug.
// - Quiet if any member disposes the field, if the field escapes (returned or
//   passed as an argument), or if the field is static.
// =============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC033: a CancellationTokenSource field that is never disposed.
/// </summary>
public class CC033_UndisposedTokenSourceField
{
    // VIOLATION (CC033 warns here) — created by this type, never disposed
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public CancellationToken Token => _cts.Token;
}

/// <summary>
/// FIXED — the owner disposes what it created.
/// </summary>
public sealed class CC033_Fixed : IDisposable
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public CancellationToken Token => _cts.Token;

    public void Dispose() => _cts.Dispose();
}

/// <summary>
/// CLEAN — the source is injected, so this type does not own it. Disposing another
/// object's source would be a bug, which is why CC033 requires creation.
/// </summary>
public class CC033_Injected
{
    private readonly CancellationTokenSource _cts;

    public CC033_Injected(CancellationTokenSource cts) => _cts = cts;

    public Task RunAsync() => Task.CompletedTask;
}
