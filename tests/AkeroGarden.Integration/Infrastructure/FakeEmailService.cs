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

    /// <summary>
    /// When set, a send to an address it returns true for fails the same way as
    /// <see cref="Delivers"/> = false. For a test where one recipient's mail goes out and another's
    /// does not — the completion nudge's guest email versus its admin alert.
    /// </summary>
    public Func<string, bool>? FailsFor { get; set; }

    public Task<bool> SendAsync(
        string toAddress, string toName, string subject, string htmlBody,
        string? replyToAddress = null, string? replyToName = null)
    {
        if (!Delivers || FailsFor?.Invoke(toAddress) == true)
            return Task.FromResult(false);

        Sent.Add(new SentEmail(toAddress, toName, subject, htmlBody, replyToAddress, replyToName));
        return Task.FromResult(true);
    }
}
