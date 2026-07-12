using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AkeroGarden.Web.Services;

public class SmtpEmailService(IOptions<SmtpOptions> options, ILogger<SmtpEmailService> logger) : IEmailService
{
    private readonly SmtpOptions _opts = options.Value;

    public async Task SendAsync(string toAddress, string toName, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        var maxAttempts = Math.Max(1, _opts.MaxSendAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await TrySendAsync(message);
                return; // sent
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
        using var client = new SmtpClient();
        var socketOptions = _opts.Port switch
        {
            1025 => SecureSocketOptions.None,        // Mailpit — no TLS
            465  => SecureSocketOptions.SslOnConnect, // One.com SSL
            _    => SecureSocketOptions.StartTls     // standard STARTTLS (587)
        };
        await client.ConnectAsync(_opts.Host, _opts.Port, socketOptions);

        if (!string.IsNullOrEmpty(_opts.Username) && _opts.Password is not null)
            await client.AuthenticateAsync(_opts.Username, _opts.Password);

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
