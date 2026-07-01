using System.Collections.Concurrent;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Integration.Infrastructure;

/// <summary>
/// Test double for <see cref="IEmailService"/> that records every send instead of contacting
/// SMTP, so integration tests can assert which booking emails were dispatched (FR-05/06).
/// </summary>
public sealed class FakeEmailService : IEmailService
{
    public sealed record SentEmail(string ToAddress, string ToName, string Subject, string HtmlBody);

    public ConcurrentBag<SentEmail> Sent { get; } = [];

    public Task SendAsync(string toAddress, string toName, string subject, string htmlBody)
    {
        Sent.Add(new SentEmail(toAddress, toName, subject, htmlBody));
        return Task.CompletedTask;
    }
}
