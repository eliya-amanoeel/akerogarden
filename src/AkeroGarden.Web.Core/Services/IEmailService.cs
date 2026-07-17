namespace AkeroGarden.Web.Core.Services;

public interface IEmailService
{
    /// <summary>Sends an email. When <paramref name="replyToAddress"/> is set, replies route
    /// there instead of the sending mailbox — e.g. an admin notification should reply to the
    /// guest who submitted the inquiry, not back to the notification address itself.</summary>
    Task SendAsync(
        string toAddress, string toName, string subject, string htmlBody,
        string? replyToAddress = null, string? replyToName = null);
}
