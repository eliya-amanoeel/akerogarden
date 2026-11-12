using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Unit;

public class SmtpEmailServiceTests
{
    // NFR-13: email failures must never propagate — log and swallow only.
    [Fact]
    public async Task SendAsync_DoesNotThrow_WhenSmtpUnreachable()
    {
        var opts = Options.Create(new SmtpOptions
        {
            Host = "localhost",
            Port = 19999, // nothing listening here
            FromAddress = "noreply@akerogarden.se",
            FromName = "Åkerögården",
            RetryDelayMs = 0 // retry immediately so the test stays fast (NFR-13 retry path)
        });
        var svc = new SmtpEmailService(opts, NullLogger<SmtpEmailService>.Instance);

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
            FromName = "Åkerögården",
            MaxSendAttempts = 1,
            RetryDelayMs = 0
        });
        var capturingLogger = new CapturingLogger<SmtpEmailService>();
        var svc = new SmtpEmailService(opts, capturingLogger);

        await svc.SendAsync("gast.namn@example.se", "Gäst Namn", "Ämne", "<p>Brödtext</p>");

        var logged = Assert.Single(capturingLogger.Messages);
        Assert.DoesNotContain("gast.namn@example.se", logged);
        Assert.DoesNotContain("gast.namn", logged);
        Assert.Contains("***@example.se", logged);
    }

    // Client review 2026-11-11, item 4: production never set Smtp__FromName, so every email went out
    // with an empty display name and mail clients showed "info". Built from options that set only
    // the address — exactly production's configuration before the fix.
    [Fact]
    public void BuildMessage_SignsAsAkerogarden_WhenNoSenderNameIsConfigured()
    {
        var opts = Options.Create(new SmtpOptions { FromAddress = "info@akerogarden.se" });
        var svc = new SmtpEmailService(opts, NullLogger<SmtpEmailService>.Instance);

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
        var svc = new SmtpEmailService(opts, NullLogger<SmtpEmailService>.Instance);

        var message = svc.BuildMessage(
            "info@akerogarden.se", "Åkerögården Admin", "Ämne", "<p>Brödtext</p>",
            "anna@example.se", "Anna Andersson", fromName: "Anna Andersson");

        var from = Assert.IsType<MimeKit.MailboxAddress>(Assert.Single(message.From));
        Assert.Equal("Anna Andersson", from.Name);
        Assert.Equal("info@akerogarden.se", from.Address);
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
