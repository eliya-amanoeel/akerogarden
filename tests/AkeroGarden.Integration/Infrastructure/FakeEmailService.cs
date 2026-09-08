using System.Collections.Concurrent;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Integration.Infrastructure;

/// <summary>
/// Test double for <see cref="IEmailService"/> that records every send instead of contacting
/// SMTP, so integration tests can assert which booking emails were dispatched (FR-05/06).
/// </summary>
public sealed class FakeEmailService : IEmailService
{
    public sealed record SentEmail(
        string ToAddress, string ToName, string Subject, string HtmlBody,
        string? ReplyToAddress = null, string? ReplyToName = null);

    public ConcurrentBag<SentEmail> Sent { get; } = [];

    /// <summary>
    /// When false, every send reports failure (and records nothing) without throwing — the shape of a
    /// real SMTP outage, which <see cref="SmtpEmailService"/> swallows and reports via its return value
    /// rather than by raising. Lets a test drive the "mail did not go out" branch that finding #11 was
    /// about. Defaults to true so existing tests are unaffected.
    /// </summary>
    public bool Delivers { get; set; } = true;

    public Task<bool> SendAsync(
        string toAddress, string toName, string subject, string htmlBody,
        string? replyToAddress = null, string? replyToName = null)
    {
        if (!Delivers)
            return Task.FromResult(false);

        Sent.Add(new SentEmail(toAddress, toName, subject, htmlBody, replyToAddress, replyToName));
        return Task.FromResult(true);
    }
}
