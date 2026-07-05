using System.Globalization;
using System.Net;
using System.Text;
using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Pure Swedish email-body builders for the booking lifecycle (requirements.md §9), unit-testable
/// without SMTP or configuration (NFR-26, NFR-32). Each method returns the subject and the main
/// HTML body; the shared footer (Åkerögården name, contact email, privacy-policy link) is added
/// by <see cref="BookingEmailService"/> at send time. All interpolated free text is HTML-encoded.
/// </summary>
public static class BookingEmailTemplates
{
    private static readonly CultureInfo Sv = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Guest acknowledgement for a stay request (§9, FR-05).</summary>
    public static EmailContent StayRequestAcknowledgement(string contactName)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Tack för din bokningsförfrågan! Vi har tagit emot din förfrågan om att boka en vistelse hos oss på Åkerögården.</p>");
        body.Append("<p>Vi går igenom din förfrågan och återkommer normalt inom några dagar med besked. Observera att förfrågan inte är en bekräftad bokning förrän vi har godkänt den.</p>");
        return new EmailContent("Vi har tagit emot din bokningsförfrågan", body.ToString());
    }

    /// <summary>Guest acknowledgement for a general inquiry (§9, FR-05).</summary>
    public static EmailContent GeneralInquiryAcknowledgement(string contactName)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Tack för ditt meddelande! Vi har tagit emot din förfrågan och återkommer så snart vi kan.</p>");
        return new EmailContent("Vi har tagit emot ditt meddelande", body.ToString());
    }

    /// <summary>Admin notification of a new inquiry, with a direct link to the booking (§9, FR-06).</summary>
    public static EmailContent AdminNewInquiry(Booking booking, string adminBookingUrl)
    {
        var isStay = booking.BookingType == BookingType.StayRequest;
        var body = new StringBuilder();
        body.Append("<p>En ny förfrågan har inkommit.</p>");
        body.Append("<ul>");
        body.Append($"<li><strong>Typ:</strong> {(isStay ? "Bokningsförfrågan" : "Allmän förfrågan")}</li>");
        body.Append($"<li><strong>Namn:</strong> {Enc(booking.ContactName)}</li>");
        body.Append($"<li><strong>E-post:</strong> {Enc(booking.ContactEmail)}</li>");
        if (!string.IsNullOrWhiteSpace(booking.ContactPhone))
            body.Append($"<li><strong>Telefon:</strong> {Enc(booking.ContactPhone)}</li>");

        if (isStay)
        {
            body.Append($"<li><strong>Incheckning:</strong> {FormatDate(booking.CheckIn)}</li>");
            body.Append($"<li><strong>Utcheckning:</strong> {FormatDate(booking.CheckOut)}</li>");
            body.Append($"<li><strong>Antal gäster (övernattning):</strong> {booking.OvernightGuestCount?.ToString(Sv) ?? "—"}</li>");
            body.Append($"<li><strong>Antal dagsgäster:</strong> {booking.DayGuestCount?.ToString(Sv) ?? "—"}</li>");
            body.Append($"<li><strong>Måltidsplan:</strong> {FormatMealPlan(booking.MealPlan)}</li>");
            if (booking.FacilitySelections.Count > 0)
                body.Append($"<li><strong>Faciliteter:</strong> {Enc(string.Join(", ", booking.FacilitySelections.Select(f => f.FacilityNameSnapshot)))}</li>");
            if (booking.CleaningSelections.Count > 0)
                body.Append($"<li><strong>Städning:</strong> {Enc(string.Join(", ", booking.CleaningSelections.Select(c => c.ZoneNameSnapshot)))}</li>");
            if (booking.IsOrganisation)
                body.Append($"<li><strong>Organisation:</strong> {Enc(booking.OrgName)} ({Enc(booking.OrgNumber)})</li>");
        }

        if (!string.IsNullOrWhiteSpace(booking.Message))
            body.Append($"<li><strong>Meddelande:</strong> {Enc(booking.Message)}</li>");
        body.Append("</ul>");
        body.Append($"<p><a href=\"{Enc(adminBookingUrl)}\">Öppna förfrågan i adminpanelen</a></p>");

        return new EmailContent(
            isStay ? "Ny bokningsförfrågan" : "Ny allmän förfrågan",
            body.ToString());
    }

    /// <summary>Admin notification that the guest has submitted the completion form (§9 "Admin —
    /// details complete", FR-61): data is ready, a link to the booking, and a reminder to download
    /// the lists (list downloads land in Phase 5).</summary>
    public static EmailContent AdminDetailsComplete(Booking booking, string adminBookingUrl)
    {
        var body = new StringBuilder();
        body.Append("<p>Gästen har slutfört sina bokningsuppgifter.</p>");
        body.Append("<ul>");
        body.Append($"<li><strong>Namn:</strong> {Enc(booking.ContactName)}</li>");
        body.Append($"<li><strong>Incheckning:</strong> {FormatDate(booking.CheckIn)}</li>");
        body.Append($"<li><strong>Utcheckning:</strong> {FormatDate(booking.CheckOut)}</li>");
        body.Append($"<li><strong>Antal deltagare:</strong> {booking.Participants.Count.ToString(Sv)}</li>");
        body.Append("</ul>");
        body.Append("<p>Uppgifterna är redo. Kom ihåg att ladda ner deltagar- och matlistorna inför vistelsen.</p>");
        body.Append($"<p><a href=\"{Enc(adminBookingUrl)}\">Öppna bokningen i adminpanelen</a></p>");

        return new EmailContent("Bokningsuppgifter inskickade", body.ToString());
    }

    /// <summary>Denial email — includes the admin's reason and a fixed closing line (§9, FR-50).</summary>
    public static EmailContent Denial(string contactName, string denialReason)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Tack för din bokningsförfrågan. Tyvärr kan vi inte bekräfta din bokning vid detta tillfälle.</p>");
        body.Append($"<p><strong>Motivering:</strong> {Enc(denialReason)}</p>");
        body.Append("<p>Kontakta oss för att diskutera alternativa datum.</p>");
        return new EmailContent("Besked om din bokningsförfrågan", body.ToString());
    }

    /// <summary>Approval email — magic link, exact expiry in plain text, deposit/cancellation policy (§9, FR-52).</summary>
    public static EmailContent Approval(string contactName, string magicLinkUrl, DateTime expiryUtc)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Din bokningsförfrågan är godkänd! För att slutföra din bokning behöver du fylla i uppgifter om deltagare och måltider.</p>");
        body.Append($"<p><a href=\"{Enc(magicLinkUrl)}\">Slutför din bokning här</a></p>");
        body.Append($"<p>Länken är giltig till och med <strong>{FormatDateTime(expiryUtc)}</strong>. Fyll i uppgifterna före dess.</p>");
        body.Append("<h3>Handpenning och avbokning</h3>");
        body.Append("<p>En handpenning på 20 % av totalsumman betalas för att bekräfta bokningen. Resterande 80 % faktureras före ankomst.</p>");
        body.Append("<p>Vid avbokning mer än två månader före ankomst återbetalas handpenningen i sin helhet. Vid avbokning två månader eller mindre före ankomst återbetalas inte handpenningen.</p>");
        body.Append("<p>Incheckning sker kl. 14:00 och utcheckning kl. 12:00.</p>");
        return new EmailContent("Din bokning är godkänd – slutför dina uppgifter", body.ToString());
    }

    /// <summary>Admin warning that a record is 30 days from automatic GDPR deletion (§9, FR-66).</summary>
    public static EmailContent PreDeletionWarning(Booking booking, string adminBookingUrl, DateOnly deleteAfter)
    {
        var isStay = booking.BookingType == BookingType.StayRequest;
        var body = new StringBuilder();
        body.Append("<p>En bokningspost är schemalagd för automatisk radering (GDPR-gallring) om 30 dagar.</p>");
        body.Append("<ul>");
        body.Append($"<li><strong>Typ:</strong> {(isStay ? "Bokningsförfrågan" : "Allmän förfrågan")}</li>");
        body.Append($"<li><strong>Namn:</strong> {Enc(booking.ContactName)}</li>");
        body.Append($"<li><strong>Status:</strong> {Enc(booking.Status.ToString())}</li>");
        if (isStay)
        {
            body.Append($"<li><strong>Incheckning:</strong> {FormatDate(booking.CheckIn)}</li>");
            body.Append($"<li><strong>Utcheckning:</strong> {FormatDate(booking.CheckOut)}</li>");
        }
        body.Append($"<li><strong>Raderas efter:</strong> {FormatDate(deleteAfter)}</li>");
        body.Append("</ul>");
        body.Append($"<p><a href=\"{Enc(adminBookingUrl)}\">Öppna bokningen i adminpanelen</a></p>");
        return new EmailContent("Bokningspost raderas snart (GDPR-gallring)", body.ToString());
    }

    /// <summary>Thank-you email sent when the admin marks the booking <c>Completed</c> (§9, FR-65).
    /// Fixed text — not customisable per booking.</summary>
    public static EmailContent ThankYou(string contactName)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Tack för att ni valde Åkerögården! Vi hoppas att ni hade en trevlig vistelse hos oss.</p>");
        body.Append("<p>Ni är varmt välkomna tillbaka. Hör gärna av er om ni vill boka ett nytt besök.</p>");
        return new EmailContent("Tack för ditt besök hos Åkerögården", body.ToString());
    }

    /// <summary>Cancellation email — deposit clause conditional on initiator and timing (§9, FR-79).</summary>
    public static EmailContent Cancellation(
        string contactName,
        CancellationInitiator initiator,
        DepositOutcome outcome)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Din bokning hos Åkerögården har avbokats.</p>");

        var clause = initiator == CancellationInitiator.PropertyInitiated
            ? "Vi återbetalar hela handpenningen."
            : outcome == DepositOutcome.FullRefund
                ? "Eftersom avbokningen sker mer än två månader före ankomst återbetalas handpenningen i sin helhet."
                : "Eftersom avbokningen sker två månader eller mindre före ankomst återbetalas inte handpenningen.";
        body.Append($"<p>{clause}</p>");
        body.Append("<p>Har du frågor är du välkommen att kontakta oss.</p>");
        return new EmailContent("Din bokning har avbokats", body.ToString());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────
    private static string Greeting(string contactName) => $"<p>Hej {Enc(contactName)},</p>";

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string FormatDate(DateOnly? date) =>
        date is null ? "—" : date.Value.ToString("d MMMM yyyy", Sv);

    private static string FormatDateTime(DateTime instant) =>
        instant.ToString("d MMMM yyyy 'kl.' HH:mm", Sv);

    private static string FormatMealPlan(MealPlan? plan) => plan switch
    {
        MealPlan.FullBoardAll => "Helpension för alla",
        MealPlan.SelfCateringAll => "Självhushåll för alla",
        MealPlan.Mixed => "Blandat",
        _ => "—"
    };
}
