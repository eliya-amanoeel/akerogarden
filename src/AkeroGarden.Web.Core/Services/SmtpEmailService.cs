using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AkeroGarden.Web.Core.Services;

public class SmtpEmailService(IOptions<SmtpOptions> options, ILogger<SmtpEmailService> logger) : IEmailService
{
    private readonly SmtpOptions _opts = options.Value;

    public async Task<bool> SendAsync(
        string toAddress, string toName, string subject, string htmlBody,
        string? replyToAddress = null, string? replyToName = null)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));
        if (!string.IsNullOrEmpty(replyToAddress))
            message.ReplyTo.Add(new MailboxAddress(replyToName, replyToAddress));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        var maxAttempts = Math.Max(1, _opts.MaxSendAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await TrySendAsync(message);
                return true; // sent
            }
            catch (Exception ex)
            {
                // NFR-13: email failure must not fail the calling operation — log and retry,
                // then give up quietly. The booking operation is never blocked by mail delivery.
                if (attempt < maxAttempts)
                {
                    logger.LogWarning(ex,
                        "Email send attempt {Attempt}/{Max} to {Address} failed (subject: {Subject}); retrying",
                        attempt, maxAttempts, MaskAddress(toAddress), subject);
                    if (_opts.RetryDelayMs > 0)
                        await Task.Delay(_opts.RetryDelayMs * attempt);
                }
                else
                {
                    logger.LogError(ex,
                        "Email send to {Address} failed after {Max} attempts (subject: {Subject}); giving up",
                        MaskAddress(toAddress), maxAttempts, subject);
                }
            }
        }

        // Every attempt failed. Reported rather than thrown, so a caller that persists "we notified
        // them" can tell the difference — see IEmailService.SendAsync (finding #11).
        return false;
    }

    /// <summary>Reduces a recipient address to its domain for logging — enough to diagnose a
    /// provider-wide delivery problem without writing a guest's email into telemetry (NFR-05/06).</summary>
    private static string MaskAddress(string address)
    {
        var at = address.IndexOf('@');
        return at < 0 ? "***" : $"***@{address[(at + 1)..]}";
    }

    private async Task TrySendAsync(MimeMessage message)
    {
        using var client = new SmtpClient
        {
            // Bound how long a single SMTP operation may hang (post-launch security review, finding
            // #12). MailKit's default is 120 seconds per operation, and the guest-facing sends are
            // awaited inline in the /boka POST — with three attempts across two messages, an
            // unresponsive One.com endpoint could hold a booking submission open for many minutes and
            // tie up request threads on a B1 instance. NFR-13 promises a mail failure never *fails* the
            // booking; it says nothing about how long it may block one. A short timeout turns a hung
            // connection into a fast, logged failure.
            Timeout = Math.Max(1, _opts.TimeoutSeconds) * 1000,
        };

        var socketOptions = _opts.Port switch
        {
            1025 => SecureSocketOptions.None,        // Mailpit — no TLS
            465 => SecureSocketOptions.SslOnConnect, // One.com SSL
            _ => SecureSocketOptions.StartTls     // standard STARTTLS (587)
        };
        await client.ConnectAsync(_opts.Host, _opts.Port, socketOptions);

        if (!string.IsNullOrEmpty(_opts.Username) && _opts.Password is not null)
            await client.AuthenticateAsync(_opts.Username, _opts.Password);

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
