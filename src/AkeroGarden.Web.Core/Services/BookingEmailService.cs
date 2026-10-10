using System.Net;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Web.Core.Services;

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
    public Task<bool> SendInquiryAcknowledgementAsync(Booking booking)
    {
        var content = booking.BookingType == BookingType.StayRequest
            ? BookingEmailTemplates.StayRequestAcknowledgement(booking.ContactName, booking.BookingReference)
            : BookingEmailTemplates.GeneralInquiryAcknowledgement(booking.ContactName, booking.BookingReference);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Admin notification of a new inquiry with a direct link to the booking (FR-06).
    /// Reply-To is the guest's own address — hitting "Reply" in the mail client should reach the
    /// guest who submitted the inquiry, not bounce back to the notification mailbox itself.</summary>
    public Task<bool> SendAdminNewInquiryAsync(Booking booking)
    {
        var content = BookingEmailTemplates.AdminNewInquiry(booking, BuildAdminBookingUrl(booking.Id));
        return SendToAdminAsync(content, booking);
    }

    /// <summary>Admin notification that the guest submitted the completion form (FR-61). Requires
    /// <see cref="Booking.Participants"/> to be loaded for the participant count.</summary>
    public Task<bool> SendAdminDetailsCompleteAsync(Booking booking)
    {
        var content = BookingEmailTemplates.AdminDetailsComplete(booking, BuildAdminBookingUrl(booking.Id));
        return SendToAdminAsync(content, booking);
    }

    /// <summary>Denial email to the guest, including the admin's reason (FR-50).</summary>
    public Task<bool> SendDenialAsync(Booking booking)
    {
        var content = BookingEmailTemplates.Denial(booking.ContactName, booking.DenialReason ?? string.Empty, booking.BookingReference);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>The assigned lodging buildings by name (FR-103), read from the assignment
    /// <b>snapshots</b> so the guest is told what was agreed even if the building was renamed since
    /// (NFR-16). Requires <see cref="Booking.BuildingAssignments"/> to be loaded — an unloaded
    /// collection yields an empty list, which the templates render as no lodging line at all.</summary>
    private static IReadOnlyList<string> AssignedBuildingNames(Booking booking) =>
        booking.BuildingAssignments
            .Select(a => a.BuildingNameSnapshot)
            .OrderBy(n => n, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>Approval email (FR-52). Carries the completion magic link and its exact expiry when
    /// one was issued; a booking approved more than 40 days before check-in has no token yet
    /// (FR-51/108) and the email says the link follows later instead.
    ///
    /// <para>The variant is decided from the booking's own state rather than a parameter, so an
    /// unissued link cannot be reported to the guest as an issued one by a caller that got the
    /// argument wrong.</para></summary>
    public Task<bool> SendApprovalAsync(Booking booking)
    {
        var hasLink = !string.IsNullOrEmpty(booking.CompletionToken) && booking.CompletionTokenExpiry is not null;

        var content = BookingEmailTemplates.Approval(
            booking.ContactName,
            hasLink ? BuildCompletionUrl(booking.CompletionToken!) : null,
            hasLink ? booking.CompletionTokenExpiry : null,
            booking.BookingReference,
            AssignedBuildingNames(booking),
            booking.CheckInTime,
            booking.CheckOutTime,
            BuildTermsUrl(),
            depositRequested: booking.DepositDueDate is not null,
            estimatedPrice: booking.EstimatedPriceInclVat);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Tells the guest the completion form is now open (FR-108) — sent by the daily issuance
    /// job when check-in comes within 40 days of a booking approved too far ahead to be given a link
    /// at the time, and by the admin's manual resend (FR-49). Requires
    /// <see cref="Booking.CompletionToken"/> and <see cref="Booking.CompletionTokenExpiry"/> to be set.</summary>
    public Task<bool> SendCompletionLinkAvailableAsync(Booking booking)
    {
        if (string.IsNullOrEmpty(booking.CompletionToken) || booking.CompletionTokenExpiry is null)
            throw new InvalidOperationException("Länkmejlet kräver en genererad slutförandelänk och utgångstid.");

        var content = BookingEmailTemplates.CompletionLinkAvailable(
            booking.ContactName,
            BuildCompletionUrl(booking.CompletionToken),
            booking.CompletionTokenExpiry.Value,
            booking.BookingReference);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>The guest's own confirmation of what they submitted (FR-107), sent alongside — never
    /// instead of — the admin notification (FR-61). Requires <see cref="Booking.Participants"/>,
    /// <see cref="Booking.MealSelections"/> and <see cref="Booking.BuildingAssignments"/> to be
    /// loaded; each one silently renders as "none" rather than failing, so the caller must load
    /// them deliberately.</summary>
    public Task<bool> SendGuestDetailsReceivedAsync(Booking booking)
    {
        var content = BookingEmailTemplates.GuestDetailsReceived(
            booking, AssignedBuildingNames(booking), _opts.PropertyAddress);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Standard 14-day reminder to the guest (FR-64). Requires <see cref="Booking.Participants"/>
    /// to be loaded for the counts.</summary>
    public Task<bool> SendTwoWeekReminderAsync(Booking booking)
    {
        var overnight = booking.Participants.Count(p => p.ParticipantType == ParticipantType.Overnight);
        var dayGuests = booking.Participants.Count(p => p.ParticipantType == ParticipantType.DayGuest);
        var meals = booking.MealSelections.Count;
        var content = BookingEmailTemplates.TwoWeekReminder(
            booking.ContactName, overnight, dayGuests, meals, booking.CheckIn, booking.CheckOut,
            booking.CheckInTime, booking.CheckOutTime,
            _opts.PropertyAddress, booking.BookingReference, AssignedBuildingNames(booking));
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Completion nudge to a guest who has not yet submitted 14 days before check-in (FR-63).
    /// Requires the (freshly regenerated) <see cref="Booking.CompletionToken"/>/expiry to be set.</summary>
    public Task<bool> SendCompletionNudgeAsync(Booking booking)
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
    public Task<bool> SendAdminGuestNotCompletedAsync(Booking booking)
    {
        if (booking.CompletionTokenExpiry is null)
            throw new InvalidOperationException("Adminvarning kräver en satt utgångstid för slutförandelänken.");

        var content = BookingEmailTemplates.AdminGuestNotCompleted(
            booking, BuildAdminBookingUrl(booking.Id), booking.CompletionTokenExpiry.Value);
        return SendToAdminAsync(content, booking);
    }

    /// <summary>Admin warning 30 days before a record is auto-deleted (FR-66). Requires
    /// <see cref="Booking.DeleteAfter"/> to be set.</summary>
    public Task<bool> SendPreDeletionWarningAsync(Booking booking)
    {
        if (booking.DeleteAfter is null)
            throw new InvalidOperationException("Gallringsvarning kräver ett satt raderingsdatum (DeleteAfter).");

        var content = BookingEmailTemplates.PreDeletionWarning(
            booking, BuildAdminBookingUrl(booking.Id), booking.DeleteAfter.Value);
        return SendToAdminAsync(content);
    }

    /// <summary>Standard fixed thank-you email when the admin marks the booking Completed (FR-65).</summary>
    public Task<bool> SendThankYouAsync(Booking booking)
    {
        var content = BookingEmailTemplates.ThankYou(booking.ContactName, booking.BookingReference);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Cancellation email (FR-79). <paramref name="notice"/> and
    /// <paramref name="depositPaid"/> come from the cancellation itself — the status it was cancelled
    /// from and the admin's answer about the deposit are not on the booking afterwards.</summary>
    public Task<bool> SendCancellationAsync(Booking booking, CancellationNotice notice, bool depositPaid)
    {
        var content = BookingEmailTemplates.Cancellation(
            booking.ContactName, notice, depositPaid, booking.BookingReference, BuildTermsUrl());
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Deposit request (FR-115). Requires <see cref="Booking.DepositDueDate"/> and
    /// <see cref="Booking.EstimatedPriceInclVat"/> to be set and <see cref="Booking.BuildingAssignments"/>
    /// loaded (the lodging line names them).</summary>
    public Task<bool> SendDepositRequestAsync(Booking booking)
    {
        if (booking.DepositDueDate is null)
            throw new InvalidOperationException("Begäran om handpenning kräver ett satt förfallodatum.");
        if (booking.EstimatedPriceInclVat is null)
            throw new InvalidOperationException("Begäran om handpenning kräver ett beräknat pris.");

        var content = BookingEmailTemplates.DepositRequest(
            booking.ContactName, booking.BookingReference, booking.CheckIn, booking.CheckOut,
            AssignedBuildingNames(booking), booking.DepositDueDate.Value, _opts.Payment, BuildTermsUrl(),
            booking.EstimatedPriceInclVat.Value);
        return SendToGuestAsync(booking, content);
    }

    /// <summary>Deposit reminder (FR-116) — the nightly job's one reminder past the due date.</summary>
    public Task<bool> SendDepositReminderAsync(Booking booking)
    {
        if (booking.DepositDueDate is null)
            throw new InvalidOperationException("Påminnelse om handpenning kräver ett satt förfallodatum.");

        var content = BookingEmailTemplates.DepositReminder(
            booking.ContactName, booking.BookingReference, booking.DepositDueDate.Value, _opts.Payment, BuildTermsUrl(),
            booking.EstimatedPriceInclVat);
        return SendToGuestAsync(booking, content);
    }

    // ── Dispatch ────────────────────────────────────────────────────────────────
    private Task<bool> SendToGuestAsync(Booking booking, EmailContent content) =>
        email.SendAsync(booking.ContactEmail, booking.ContactName, content.Subject, WithFooter(content.HtmlBody));

    private Task<bool> SendToAdminAsync(EmailContent content, Booking? replyToBooking = null) =>
        email.SendAsync(
            _opts.AdminNotificationEmail, _opts.AdminNotificationName, content.Subject, WithFooter(content.HtmlBody),
            replyToBooking?.ContactEmail, replyToBooking?.ContactName);

    private string BuildCompletionUrl(string token) =>
        $"{_opts.PublicBaseUrl.TrimEnd('/')}/slutfor/{token}";

    private string BuildTermsUrl() =>
        $"{_opts.PublicBaseUrl.TrimEnd('/')}/bokningsvillkor";

    private string BuildAdminBookingUrl(Guid bookingId) =>
        $"{_opts.AdminBaseUrl.TrimEnd('/')}/bokningar/{bookingId}";

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
