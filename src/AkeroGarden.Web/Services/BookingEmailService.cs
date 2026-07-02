using System.Net;
using AkeroGarden.Web.Data.Entities;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Sends the booking-lifecycle emails of §9. Builds each body from the pure
/// <see cref="BookingEmailTemplates"/>, appends the shared footer (Åkerögården name, contact
/// email, privacy-policy link), and dispatches via <see cref="IEmailService"/> — whose failures
/// are logged and never propagate (NFR-13), so no email failure blocks a booking operation.
/// </summary>
public class BookingEmailService(IEmailService email, IOptions<BookingEmailOptions> options)
{
    private readonly BookingEmailOptions _opts = options.Value;

    /// <summary>Guest acknowledgement immediately after an inquiry is submitted (FR-05).
    /// Content differs by booking type.</summary>
    public Task SendInquiryAcknowledgementAsync(Booking booking)
    {
        var content = booking.BookingType == BookingType.StayRequest
            ? BookingEmailTemplates.StayRequestAcknowledgement(booking.ContactName)
            : BookingEmailTemplates.GeneralInquiryAcknowledgement(booking.ContactName);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Admin notification of a new inquiry with a direct link to the booking (FR-06).</summary>
    public Task SendAdminNewInquiryAsync(Booking booking)
    {
        var content = BookingEmailTemplates.AdminNewInquiry(booking, BuildAdminBookingUrl(booking.Id));
        return SendToAdminAsync(content);
    }

    /// <summary>Denial email to the guest, including the admin's reason (FR-50).</summary>
    public Task SendDenialAsync(Booking booking)
    {
        var content = BookingEmailTemplates.Denial(booking.ContactName, booking.DenialReason ?? string.Empty);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Approval email with the completion magic link and its exact expiry (FR-52).
    /// Requires <see cref="Booking.CompletionToken"/> and <see cref="Booking.CompletionTokenExpiry"/> to be set.</summary>
    public Task SendApprovalAsync(Booking booking)
    {
        if (string.IsNullOrEmpty(booking.CompletionToken) || booking.CompletionTokenExpiry is null)
            throw new InvalidOperationException("Godkännandemejl kräver en genererad slutförandelänk och utgångstid.");

        var content = BookingEmailTemplates.Approval(
            booking.ContactName,
            BuildCompletionUrl(booking.CompletionToken),
            booking.CompletionTokenExpiry.Value);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Cancellation email with the deposit clause resolved per initiator and timing (FR-79).</summary>
    public Task SendCancellationAsync(Booking booking)
    {
        if (booking.CancellationInitiator is null)
            throw new InvalidOperationException("Avbokningsmejl kräver att avbokningens initiativtagare är satt.");

        // Guest-initiated timing is judged from the cancellation date against check-in (§5.6).
        var cancelledOn = DateOnly.FromDateTime(booking.CancelledAt ?? DateTime.UtcNow);
        var outcome = booking.CheckIn is null
            ? DepositOutcome.FullRefund // no dated stay to judge timing against; default to full refund
            : CancellationPolicy.ResolveDepositOutcome(booking.CancellationInitiator.Value, booking.CheckIn.Value, cancelledOn);

        var content = BookingEmailTemplates.Cancellation(booking.ContactName, booking.CancellationInitiator.Value, outcome);
        return SendToGuestAsync(booking, content);
    }

    // ── Dispatch ────────────────────────────────────────────────────────────────
    private Task SendToGuestAsync(Booking booking, EmailContent content) =>
        email.SendAsync(booking.ContactEmail, booking.ContactName, content.Subject, WithFooter(content.HtmlBody));

    private Task SendToAdminAsync(EmailContent content) =>
        email.SendAsync(_opts.AdminNotificationEmail, _opts.AdminNotificationName, content.Subject, WithFooter(content.HtmlBody));

    private string BuildCompletionUrl(string token) =>
        $"{_opts.PublicBaseUrl.TrimEnd('/')}/slutfor/{token}";

    private string BuildAdminBookingUrl(Guid bookingId) =>
        $"{_opts.AdminBaseUrl.TrimEnd('/')}/admin/bokningar/{bookingId}";

    /// <summary>Appends the footer every email must carry (§9): name, contact email, privacy link.</summary>
    private string WithFooter(string body)
    {
        var contactEmail = WebUtility.HtmlEncode(_opts.ContactEmail);
        var privacyUrl = WebUtility.HtmlEncode($"{_opts.PublicBaseUrl.TrimEnd('/')}/integritetspolicy");
        return body +
            "<hr>" +
            "<p style=\"font-size:12px;color:#666\">Åkerögården<br>" +
            $"Kontakt: <a href=\"mailto:{contactEmail}\">{contactEmail}</a><br>" +
            $"<a href=\"{privacyUrl}\">Integritetspolicy</a></p>";
    }
}
