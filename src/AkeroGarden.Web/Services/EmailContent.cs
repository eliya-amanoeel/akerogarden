namespace AkeroGarden.Web.Services;

/// <summary>
/// A composed email ready to send: the subject line and the HTML body. Produced by the pure
/// template builders in <see cref="BookingEmailTemplates"/> (unit-testable without SMTP) and
/// handed to <see cref="IEmailService"/> by <see cref="BookingEmailService"/>.
/// </summary>
public readonly record struct EmailContent(string Subject, string HtmlBody);
