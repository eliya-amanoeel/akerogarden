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

        try
        {
            using var client = new SmtpClient();
            // Port 1025 (Mailpit dev) needs no TLS; production uses STARTTLS on 587.
            var socketOptions = _opts.Port == 1025 ? SecureSocketOptions.None : SecureSocketOptions.StartTls;
            await client.ConnectAsync(_opts.Host, _opts.Port, socketOptions);

            if (!string.IsNullOrEmpty(_opts.Username) && _opts.Password is not null)
                await client.AuthenticateAsync(_opts.Username, _opts.Password);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            // NFR-13: email failure must not fail the calling operation — log and continue.
            logger.LogError(ex, "Failed to send email to {Address} (subject: {Subject})", toAddress, subject);
        }
    }
}
