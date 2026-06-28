using AkeroGarden.Web.Services;
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
            FromName = "Åkerögården"
        });
        var svc = new SmtpEmailService(opts, NullLogger<SmtpEmailService>.Instance);

        var ex = await Record.ExceptionAsync(() =>
            svc.SendAsync("mottagare@test.se", "Mottagare", "Ämne", "<p>Brödtext</p>"));

        Assert.Null(ex);
    }
}
