using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Unit;

public class SmtpEmailServiceTests
{
    // The signature's links are built from the environment's public site URL.
    private static readonly IOptions<SiteOptions> Site = Options.Create(new SiteOptions
    {
        PublicBaseUrl = "https://akerogarden.se",
    });

    // NFR-13: email failures must never propagate — log and swallow only.
    [Fact]
    public async Task SendAsync_DoesNotThrow_WhenSmtpUnreachable()
    {
        var opts = Options.Create(new SmtpOptions
        {
            Host = "localhost",
            Port = 19999, // nothing listening here
            FromAddress = "noreply@akerogarden.se",
            RetryDelayMs = 0 // retry immediately so the test stays fast (NFR-13 retry path)
        });
        var svc = new SmtpEmailService(opts, Site, NullLogger<SmtpEmailService>.Instance);

        var ex = await Record.ExceptionAsync(() =>
            svc.SendAsync("mottagare@test.se", "Mottagare", "Ämne", "<p>Brödtext</p>"));

        Assert.Null(ex);
    }

    // Phase 11 (NFR-05/06): a send failure must not write the guest's full email address into
    // telemetry — only the domain, enough to diagnose a provider-wide delivery problem.
    [Fact]
    public async Task SendAsync_MasksRecipientAddress_InFailureLogs()
    {
        var opts = Options.Create(new SmtpOptions
        {
            Host = "localhost",
            Port = 19999, // nothing listening here
            FromAddress = "noreply@akerogarden.se",
            MaxSendAttempts = 1,
            RetryDelayMs = 0
        });
        var capturingLogger = new CapturingLogger<SmtpEmailService>();
        var svc = new SmtpEmailService(opts, Site, capturingLogger);

        await svc.SendAsync("gast.namn@example.se", "Gäst Namn", "Ämne", "<p>Brödtext</p>");

        var logged = Assert.Single(capturingLogger.Messages);
        Assert.DoesNotContain("gast.namn@example.se", logged);
        Assert.DoesNotContain("gast.namn", logged);
        Assert.Contains("***@example.se", logged);
    }

    // Every email a guest receives is from "Åkerögården" — a fact, not a setting, so no environment
    // can send one with a missing or different name (client review 2026-11-11, item 4).
    [Fact]
    public void BuildMessage_SendsAsAkerogarden_ByDefault()
    {
        var opts = Options.Create(new SmtpOptions { FromAddress = "info@akerogarden.se" });
        var svc = new SmtpEmailService(opts, Site, NullLogger<SmtpEmailService>.Instance);

        var message = svc.BuildMessage("gast@example.se", "Gäst", "Ämne", "<p>Brödtext</p>");

        var from = Assert.IsType<MimeKit.MailboxAddress>(Assert.Single(message.From));
        Assert.Equal("Åkerögården", from.Name);
        Assert.Equal("info@akerogarden.se", from.Address);
    }

    // The admin notifications go out in the guest's name; the address stays the sending mailbox.
    [Fact]
    public void BuildMessage_UsesGivenSenderName_WithTheConfiguredAddress()
    {
        var opts = Options.Create(new SmtpOptions { FromAddress = "info@akerogarden.se" });
        var svc = new SmtpEmailService(opts, Site, NullLogger<SmtpEmailService>.Instance);

        var message = svc.BuildMessage(
            "info@akerogarden.se", "Åkerögården Admin", "Ämne", "<p>Brödtext</p>",
            "anna@example.se", "Anna Andersson", fromName: "Anna Andersson");

        var from = Assert.IsType<MimeKit.MailboxAddress>(Assert.Single(message.From));
        Assert.Equal("Anna Andersson", from.Name);
        Assert.Equal("info@akerogarden.se", from.Address);
    }

    // Client review 2026-11-11, item 9: the signature is added here, where every email passes, so a
    // body that brings none of its own — the admin invite and the password reset did not — still
    // goes out signed. The body is kept as given, once, ahead of the signature.
    [Fact]
    public void BuildMessage_AppendsTheSignature_ToABodyWithoutOne()
    {
        var opts = Options.Create(new SmtpOptions { FromAddress = "info@akerogarden.se" });
        var svc = new SmtpEmailService(opts, Site, NullLogger<SmtpEmailService>.Instance);

        var html = svc.BuildMessage("ny.admin@example.se", "Ny Admin", "Ämne", "<p>Brödtext</p>").HtmlBody;

        Assert.StartsWith("<p>Brödtext</p><hr>", html);
        Assert.Equal(html.IndexOf("<p>Brödtext</p>", StringComparison.Ordinal),
            html.LastIndexOf("<p>Brödtext</p>", StringComparison.Ordinal));
        // The name is HTML-encoded like every other value the emails print (ö becomes an entity), so
        // the visible text is asserted on the decoded markup.
        Assert.Contains("Åkerögården<br>", System.Net.WebUtility.HtmlDecode(html));
        Assert.Contains("Telefon: <a href=\"tel:+46739527873\">073-952 78 73</a>", html);
        Assert.Contains("E-post: <a href=\"mailto:info@akerogarden.se\">info@akerogarden.se</a>", html);
        Assert.Contains("<a href=\"https://akerogarden.se\">akerogarden.se</a>", html);
        Assert.Contains("<a href=\"https://akerogarden.se/integritetspolicy\">Integritetspolicy</a>", html);
    }

    // The logo travels inside the message as an inline PNG, not as a link (Outlook hides linked
    // images, Gmail/Outlook don't render SVG) and not as an attachment the reader sees listed. The
    // signature's <img> must point at that exact part, or clients show a broken image.
    [Fact]
    public void BuildMessage_EmbedsTheLogoInline_ReferencedFromTheSignature()
    {
        var opts = Options.Create(new SmtpOptions { FromAddress = "info@akerogarden.se" });
        var svc = new SmtpEmailService(opts, Site, NullLogger<SmtpEmailService>.Instance);

        var message = svc.BuildMessage("gast@example.se", "Gäst", "Ämne", "<p>Brödtext</p>");

        Assert.Empty(message.Attachments);
        var logo = Assert.Single(message.BodyParts.OfType<MimeKit.MimePart>(), p => p.ContentType.MimeType == "image/png");
        Assert.False(logo.IsAttachment);
        Assert.False(string.IsNullOrEmpty(logo.ContentId));
        Assert.Contains($"<img src=\"cid:{logo.ContentId}\"", message.HtmlBody);

        using var bytes = new MemoryStream();
        logo.Content.DecodeTo(bytes);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes.ToArray()[..8]);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
