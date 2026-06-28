namespace AkeroGarden.Web.Services;

public interface IEmailService
{
    Task SendAsync(string toAddress, string toName, string subject, string htmlBody);
}
