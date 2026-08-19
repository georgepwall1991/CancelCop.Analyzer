// =============================================================================
// CC049: Avoid blocking SmtpClient.Send in async code
// =============================================================================
//
// WHY THIS MATTERS:
// SmtpClient.Send parks a thread-pool thread on an SMTP handshake. That
// wait is not a CancellationToken. SendMailAsync yields the thread.
// Token-taking SendMailAsync is .NET 5+; Framework has the tokenless form.
// Do not use the event-based SendAsync.
//
// WHY THIS IS NOT CC004:
// CC004 is HttpClient. SmtpClient.Send is a separate type.
// =============================================================================

using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC049: blocking SmtpClient.Send in async code.
/// </summary>
public class CC049_BlockingSmtpClient
{
    // VIOLATION (CC049 warns here) — parks a pooled thread on SMTP
    public async Task SendBad(
        SmtpClient client,
        MailMessage message,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Send(message);
        await Task.Yield();
    }

    // FIXED — yields the thread; token-taking SendMailAsync is .NET 5+
    public async Task SendGood(
        SmtpClient client,
        MailMessage message,
        CancellationToken cancellationToken
    )
    {
        await client.SendMailAsync(message, cancellationToken);
    }

    // CLEAN — synchronous method, no pooled thread at stake
    public void SendSync(SmtpClient client, MailMessage message)
    {
        client.Send(message);
    }
}
