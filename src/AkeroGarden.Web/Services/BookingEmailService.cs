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
            ? BookingEmailTemplates.StayRequestAcknowledgement(booking.ContactName, booking.BookingReference)
            : BookingEmailTemplates.GeneralInquiryAcknowledgement(booking.ContactName, booking.BookingReference);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Admin notification of a new inquiry with a direct link to the booking (FR-06).
    /// Reply-To is the guest's own address — hitting "Reply" in the mail client should reach the
    /// guest who submitted the inquiry, not bounce back to the notification mailbox itself.</summary>
    public Task SendAdminNewInquiryAsync(Booking booking)
    {
        var content = BookingEmailTemplates.AdminNewInquiry(booking, BuildAdminBookingUrl(booking.Id));
        return SendToAdminAsync(content, booking);
    }

    /// <summary>Admin notification that the guest submitted the completion form (FR-61). Requires
    /// <see cref="Booking.Participants"/> to be loaded for the participant count.</summary>
    public Task SendAdminDetailsCompleteAsync(Booking booking)
    {
        var content = BookingEmailTemplates.AdminDetailsComplete(booking, BuildAdminBookingUrl(booking.Id));
        return SendToAdminAsync(content, booking);
    }

    /// <summary>Denial email to the guest, including the admin's reason (FR-50).</summary>
    public Task SendDenialAsync(Booking booking)
    {
        var content = BookingEmailTemplates.Denial(booking.ContactName, booking.DenialReason ?? string.Empty, booking.BookingReference);
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
            booking.CompletionTokenExpiry.Value,
            booking.BookingReference);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Standard 14-day reminder to the guest (FR-64). Requires <see cref="Booking.Participants"/>
    /// to be loaded for the counts.</summary>
    public Task SendTwoWeekReminderAsync(Booking booking)
    {
        var overnight = booking.Participants.Count(p => p.ParticipantType == ParticipantType.Overnight);
        var dayGuests = booking.Participants.Count(p => p.ParticipantType == ParticipantType.DayGuest);
        var meals = booking.Participants.Sum(p => p.MealPreferences.Count);
        var content = BookingEmailTemplates.TwoWeekReminder(
            booking.ContactName, overnight, dayGuests, meals, booking.CheckIn, booking.CheckOut,
            _opts.PropertyAddress, booking.BookingReference);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Completion nudge to a guest who has not yet submitted 14 days before check-in (FR-63).
    /// Requires the (freshly regenerated) <see cref="Booking.CompletionToken"/>/expiry to be set.</summary>
    public Task SendCompletionNudgeAsync(Booking booking)
    {
        if (string.IsNullOrEmpty(booking.CompletionToken) || booking.CompletionTokenExpiry is null)
            throw new InvalidOperationException("Påminnelse kräver en genererad slutförandelänk och utgångstid.");

        var content = BookingEmailTemplates.CompletionNudge(
            booking.ContactName, BuildCompletionUrl(booking.CompletionToken), booking.CompletionTokenExpiry.Value,
            booking.BookingReference);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Admin alert that a guest has not completed 14 days before check-in (FR-63).
    /// Requires <see cref="Booking.CompletionTokenExpiry"/> (the new link expiry) to be set.</summary>
    public Task SendAdminGuestNotCompletedAsync(Booking booking)
    {
        if (booking.CompletionTokenExpiry is null)
            throw new InvalidOperationException("Adminvarning kräver en satt utgångstid för slutförandelänken.");

        var content = BookingEmailTemplates.AdminGuestNotCompleted(
            booking, BuildAdminBookingUrl(booking.Id), booking.CompletionTokenExpiry.Value);
        return SendToAdminAsync(content, booking);
    }

    /// <summary>Admin warning 30 days before a record is auto-deleted (FR-66). Requires
    /// <see cref="Booking.DeleteAfter"/> to be set.</summary>
    public Task SendPreDeletionWarningAsync(Booking booking)
    {
        if (booking.DeleteAfter is null)
            throw new InvalidOperationException("Gallringsvarning kräver ett satt raderingsdatum (DeleteAfter).");

        var content = BookingEmailTemplates.PreDeletionWarning(
            booking, BuildAdminBookingUrl(booking.Id), booking.DeleteAfter.Value);
        return SendToAdminAsync(content);
    }

    /// <summary>Standard fixed thank-you email when the admin marks the booking Completed (FR-65).</summary>
    public Task SendThankYouAsync(Booking booking)
    {
        var content = BookingEmailTemplates.ThankYou(booking.ContactName, booking.BookingReference);
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

        var content = BookingEmailTemplates.Cancellation(
            booking.ContactName, booking.CancellationInitiator.Value, outcome, booking.BookingReference);
        return SendToGuestAsync(booking, content);
    }

    // ── Dispatch ────────────────────────────────────────────────────────────────
    private Task SendToGuestAsync(Booking booking, EmailContent content) =>
        email.SendAsync(booking.ContactEmail, booking.ContactName, content.Subject, WithFooter(content.HtmlBody));

    private Task SendToAdminAsync(EmailContent content, Booking? replyToBooking = null) =>
        email.SendAsync(
            _opts.AdminNotificationEmail, _opts.AdminNotificationName, content.Subject, WithFooter(content.HtmlBody),
            replyToBooking?.ContactEmail, replyToBooking?.ContactName);

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
