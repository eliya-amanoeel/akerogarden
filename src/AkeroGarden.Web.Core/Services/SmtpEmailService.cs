using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AkeroGarden.Web.Core.Services;

public class SmtpEmailService(
    IOptions<SmtpOptions> options, IOptions<BookingEmailOptions> emailOptions, ILogger<SmtpEmailService> logger)
    : IEmailService
{
    private readonly SmtpOptions _opts = options.Value;
    private readonly BookingEmailOptions _contact = emailOptions.Value;

    private const string LogoResourceName = "AkeroGarden.email-signature.png";

    /// <summary>The signature logo, read once from the assembly. A PNG rendered at 2× from
    /// <c>wwwroot/logo/lockup-transparent.svg</c> on a white ground — see <see cref="AppendSignature"/>.</summary>
    private static readonly Lazy<byte[]> Logo = new(() =>
    {
        using var stream = typeof(SmtpEmailService).Assembly.GetManifestResourceStream(LogoResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {LogoResourceName} is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    });

    public async Task<bool> SendAsync(
        string toAddress, string toName, string subject, string htmlBody,
        string? replyToAddress = null, string? replyToName = null, string? fromName = null)
    {
        var message = BuildMessage(toAddress, toName, subject, htmlBody, replyToAddress, replyToName, fromName);

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

    /// <summary>
    /// Builds the message exactly as <see cref="SendAsync"/> sends it, signature and logo included.
    /// Separate from the send so what every email carries — the sender name, the signature — can be
    /// asserted without an SMTP server.
    /// </summary>
    public MimeMessage BuildMessage(
        string toAddress, string toName, string subject, string htmlBody,
        string? replyToAddress = null, string? replyToName = null, string? fromName = null)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(fromName) ? _opts.FromName : fromName, _opts.FromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));
        if (!string.IsNullOrEmpty(replyToAddress))
            message.ReplyTo.Add(new MailboxAddress(replyToName, replyToAddress));
        message.Subject = subject;

        var body = new BodyBuilder();
        var logo = body.LinkedResources.Add("akerogarden.png", Logo.Value, new ContentType("image", "png"));
        logo.ContentId = MimeKit.Utils.MimeUtils.GenerateMessageId();
        body.HtmlBody = AppendSignature(htmlBody, logo.ContentId);
        message.Body = body.ToMessageBody();
        return message;
    }

    /// <summary>
    /// Appends the signature every email carries (§9; client review 2026-11-11, item 9). It lives here,
    /// in the one place every email passes through, so no sender — present or future — can leave it
    /// off; callers pass only their own body.
    ///
    /// <para><strong>Why the logo is an embedded PNG.</strong> Gmail and Outlook don't render SVG, and
    /// Outlook hides linked images until the reader clicks to load them. A linked image would also be
    /// fetched from our server each time the email is opened — a tracking pixel in all but intent. So
    /// it travels inside the message as an inline part, referenced by <c>cid:</c>. Its ground is
    /// white, not transparent: a mail client in dark mode would otherwise put the green wordmark on
    /// a dark background, where it can't be read.</para>
    /// </summary>
    private string AppendSignature(string htmlBody, string logoContentId)
    {
        var phone = WebUtility.HtmlEncode(_contact.ContactPhone);
        var phoneTel = WebUtility.HtmlEncode(_contact.ContactPhoneTel);
        var contactEmail = WebUtility.HtmlEncode(_contact.ContactEmail);
        var siteUrl = _contact.PublicBaseUrl.TrimEnd('/');
        var siteName = WebUtility.HtmlEncode(Uri.TryCreate(siteUrl, UriKind.Absolute, out var site) ? site.Host : siteUrl);
        var privacyUrl = WebUtility.HtmlEncode($"{siteUrl}/integritetspolicy");
        siteUrl = WebUtility.HtmlEncode(siteUrl);

        return htmlBody +
            "<hr>" +
            $"<p><img src=\"cid:{logoContentId}\" alt=\"Åkerögården\" width=\"192\" height=\"52\"></p>" +
            "<p style=\"font-size:12px;color:#666\">Åkerögården<br>" +
            $"Telefon: <a href=\"tel:{phoneTel}\">{phone}</a><br>" +
            $"E-post: <a href=\"mailto:{contactEmail}\">{contactEmail}</a><br>" +
            $"<a href=\"{siteUrl}\">{siteName}</a><br>" +
            $"<a href=\"{privacyUrl}\">Integritetspolicy</a></p>";
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
