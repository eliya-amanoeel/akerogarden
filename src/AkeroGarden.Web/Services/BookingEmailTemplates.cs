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
    public static EmailContent StayRequestAcknowledgement(string contactName, string bookingReference)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Tack för din bokningsförfrågan! Vi har tagit emot din förfrågan om att boka en vistelse hos oss på Åkerögården.</p>");
        body.Append(ReferenceLine(bookingReference));
        body.Append("<p>Vi går igenom din förfrågan och återkommer normalt inom några dagar med besked. Observera att förfrågan inte är en bekräftad bokning förrän vi har godkänt den.</p>");
        return new EmailContent(WithReference("Vi har tagit emot din bokningsförfrågan", bookingReference), body.ToString());
    }

    /// <summary>Guest acknowledgement for a general inquiry (§9, FR-05).</summary>
    public static EmailContent GeneralInquiryAcknowledgement(string contactName, string bookingReference)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Tack för ditt meddelande! Vi har tagit emot din förfrågan och återkommer så snart vi kan.</p>");
        body.Append(ReferenceLine(bookingReference));
        return new EmailContent(WithReference("Vi har tagit emot ditt meddelande", bookingReference), body.ToString());
    }

    /// <summary>Admin notification of a new inquiry, with a direct link to the booking (§9, FR-06).</summary>
    public static EmailContent AdminNewInquiry(Booking booking, string adminBookingUrl)
    {
        var isStay = booking.BookingType == BookingType.StayRequest;
        var body = new StringBuilder();
        body.Append("<p>En ny förfrågan har inkommit.</p>");
        body.Append("<ul>");
        body.Append($"<li><strong>Referensnummer:</strong> {Enc(booking.BookingReference)}</li>");
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
            WithReference(isStay ? "Ny bokningsförfrågan" : "Ny allmän förfrågan", booking.BookingReference),
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
        body.Append($"<li><strong>Referensnummer:</strong> {Enc(booking.BookingReference)}</li>");
        body.Append($"<li><strong>Namn:</strong> {Enc(booking.ContactName)}</li>");
        body.Append($"<li><strong>Incheckning:</strong> {FormatDate(booking.CheckIn)}</li>");
        body.Append($"<li><strong>Utcheckning:</strong> {FormatDate(booking.CheckOut)}</li>");
        body.Append($"<li><strong>Antal deltagare:</strong> {booking.Participants.Count.ToString(Sv)}</li>");
        body.Append("</ul>");
        body.Append("<p>Uppgifterna är redo. Kom ihåg att ladda ner deltagar- och matlistorna inför vistelsen.</p>");
        body.Append($"<p><a href=\"{Enc(adminBookingUrl)}\">Öppna bokningen i adminpanelen</a></p>");

        return new EmailContent(WithReference("Bokningsuppgifter inskickade", booking.BookingReference), body.ToString());
    }

    /// <summary>Denial email — includes the admin's reason and a fixed closing line (§9, FR-50).</summary>
    public static EmailContent Denial(string contactName, string denialReason, string bookingReference)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Tack för din bokningsförfrågan. Tyvärr kan vi inte bekräfta din bokning vid detta tillfälle.</p>");
        body.Append(ReferenceLine(bookingReference));
        body.Append($"<p><strong>Motivering:</strong> {Enc(denialReason)}</p>");
        body.Append("<p>Kontakta oss för att diskutera alternativa datum.</p>");
        return new EmailContent(WithReference("Besked om din bokningsförfrågan", bookingReference), body.ToString());
    }

    /// <summary>Approval email — magic link, exact expiry in plain text, deposit/cancellation policy (§9, FR-52).</summary>
    public static EmailContent Approval(string contactName, string magicLinkUrl, DateTime expiryUtc, string bookingReference)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Din bokningsförfrågan är godkänd! För att slutföra din bokning behöver du fylla i uppgifter om deltagare och måltider.</p>");
        body.Append(ReferenceLine(bookingReference));
        body.Append($"<p><a href=\"{Enc(magicLinkUrl)}\">Slutför din bokning här</a></p>");
        body.Append($"<p>Länken är giltig till och med <strong>{FormatDateTime(expiryUtc)}</strong>. Fyll i uppgifterna före dess.</p>");
        body.Append("<h3>Handpenning och avbokning</h3>");
        body.Append("<p>En handpenning på 20 % av totalsumman betalas för att bekräfta bokningen. Resterande 80 % faktureras före ankomst.</p>");
        body.Append("<p>Vid avbokning mer än två månader före ankomst återbetalas handpenningen i sin helhet. Vid avbokning två månader eller mindre före ankomst återbetalas inte handpenningen.</p>");
        body.Append("<p>Incheckning sker kl. 14:00 och utcheckning kl. 12:00.</p>");
        return new EmailContent(WithReference("Din bokning är godkänd – slutför dina uppgifter", bookingReference), body.ToString());
    }

    /// <summary>Standard 14-day reminder to the guest (§9, FR-64) — participant/day-guest counts,
    /// meal summary, check-in 14:00 / check-out 12:00, and the property address. Sent when the
    /// booking is <c>DetailsComplete</c> or later.</summary>
    public static EmailContent TwoWeekReminder(
        string contactName,
        int overnightCount,
        int dayGuestCount,
        int fullBoardMealCount,
        DateOnly? checkIn,
        DateOnly? checkOut,
        string propertyAddress,
        string bookingReference)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Snart är det dags för er vistelse hos Åkerögården! Här är en påminnelse med era uppgifter.</p>");
        body.Append("<ul>");
        body.Append($"<li><strong>Referensnummer:</strong> {Enc(bookingReference)}</li>");
        body.Append($"<li><strong>Incheckning:</strong> {FormatDate(checkIn)} kl. 14:00</li>");
        body.Append($"<li><strong>Utcheckning:</strong> {FormatDate(checkOut)} kl. 12:00</li>");
        body.Append($"<li><strong>Antal deltagare (övernattning):</strong> {overnightCount.ToString(Sv)}</li>");
        body.Append($"<li><strong>Antal dagsgäster:</strong> {dayGuestCount.ToString(Sv)}</li>");
        body.Append($"<li><strong>Antal beställda måltider:</strong> {fullBoardMealCount.ToString(Sv)}</li>");
        body.Append($"<li><strong>Adress:</strong> {Enc(propertyAddress)}</li>");
        body.Append("</ul>");
        body.Append("<p>Har ni frågor inför ankomsten är ni välkomna att kontakta oss.</p>");
        return new EmailContent(WithReference("Påminnelse inför er vistelse hos Åkerögården", bookingReference), body.ToString());
    }

    /// <summary>Completion nudge to a guest who has not yet submitted their details 14 days before
    /// check-in (§9, FR-63) — carries a freshly regenerated link and its exact expiry.</summary>
    public static EmailContent CompletionNudge(string contactName, string magicLinkUrl, DateTime expiryUtc, string bookingReference)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Ni har ännu inte slutfört era bokningsuppgifter (deltagare och måltider) inför er vistelse hos Åkerögården. Snart är det dags för incheckning – fyll gärna i uppgifterna så snart som möjligt.</p>");
        body.Append(ReferenceLine(bookingReference));
        body.Append($"<p><a href=\"{Enc(magicLinkUrl)}\">Slutför din bokning här</a></p>");
        body.Append($"<p>Länken är giltig till och med <strong>{FormatDateTime(expiryUtc)}</strong>.</p>");
        return new EmailContent(WithReference("Påminnelse: slutför dina bokningsuppgifter", bookingReference), body.ToString());
    }

    /// <summary>Admin alert that a guest has not completed their details 14 days before check-in
    /// (§9, FR-63) — link to the booking and the new completion-link expiry.</summary>
    public static EmailContent AdminGuestNotCompleted(Booking booking, string adminBookingUrl, DateTime newExpiryUtc)
    {
        var body = new StringBuilder();
        body.Append("<p>En gäst har inte slutfört sina bokningsuppgifter och incheckning närmar sig – överväg att kontakta gästen direkt.</p>");
        body.Append("<ul>");
        body.Append($"<li><strong>Referensnummer:</strong> {Enc(booking.BookingReference)}</li>");
        body.Append($"<li><strong>Namn:</strong> {Enc(booking.ContactName)}</li>");
        body.Append($"<li><strong>E-post:</strong> {Enc(booking.ContactEmail)}</li>");
        body.Append($"<li><strong>Incheckning:</strong> {FormatDate(booking.CheckIn)}</li>");
        body.Append($"<li><strong>Ny länk giltig till:</strong> {FormatDateTime(newExpiryUtc)}</li>");
        body.Append("</ul>");
        body.Append($"<p><a href=\"{Enc(adminBookingUrl)}\">Öppna bokningen i adminpanelen</a></p>");
        return new EmailContent(WithReference("Gäst har inte slutfört sin bokning", booking.BookingReference), body.ToString());
    }

    /// <summary>Admin warning that a record is 30 days from automatic GDPR deletion (§9, FR-66).</summary>
    public static EmailContent PreDeletionWarning(Booking booking, string adminBookingUrl, DateOnly deleteAfter)
    {
        var isStay = booking.BookingType == BookingType.StayRequest;
        var body = new StringBuilder();
        body.Append("<p>En bokningspost är schemalagd för automatisk radering (GDPR-gallring) om 30 dagar.</p>");
        body.Append("<ul>");
        body.Append($"<li><strong>Referensnummer:</strong> {Enc(booking.BookingReference)}</li>");
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
        return new EmailContent(WithReference("Bokningspost raderas snart (GDPR-gallring)", booking.BookingReference), body.ToString());
    }

    /// <summary>Thank-you email sent when the admin marks the booking <c>Completed</c> (§9, FR-65).
    /// Fixed text — not customisable per booking.</summary>
    public static EmailContent ThankYou(string contactName, string bookingReference)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Tack för att ni valde Åkerögården! Vi hoppas att ni hade en trevlig vistelse hos oss.</p>");
        body.Append(ReferenceLine(bookingReference));
        body.Append("<p>Ni är varmt välkomna tillbaka. Hör gärna av er om ni vill boka ett nytt besök.</p>");
        return new EmailContent(WithReference("Tack för ditt besök hos Åkerögården", bookingReference), body.ToString());
    }

    /// <summary>Cancellation email — deposit clause conditional on initiator and timing (§9, FR-79).</summary>
    public static EmailContent Cancellation(
        string contactName,
        CancellationInitiator initiator,
        DepositOutcome outcome,
        string bookingReference)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Din bokning hos Åkerögården har avbokats.</p>");
        body.Append(ReferenceLine(bookingReference));

        var clause = initiator == CancellationInitiator.PropertyInitiated
            ? "Vi återbetalar hela handpenningen."
            : outcome == DepositOutcome.FullRefund
                ? "Eftersom avbokningen sker mer än två månader före ankomst återbetalas handpenningen i sin helhet."
                : "Eftersom avbokningen sker två månader eller mindre före ankomst återbetalas inte handpenningen.";
        body.Append($"<p>{clause}</p>");
        body.Append("<p>Har du frågor är du välkommen att kontakta oss.</p>");
        return new EmailContent(WithReference("Din bokning har avbokats", bookingReference), body.ToString());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────
    private static string Greeting(string contactName) => $"<p>Hej {Enc(contactName)},</p>";

    /// <summary>Renders the shared "Referensnummer: AG-2026-0042" line every guest email carries so
    /// the guest can quote it on a support call (FR-92). "Referensnummer" rather than
    /// "Bokningsreferens" — a general inquiry isn't a booking, so a booking-specific label read
    /// wrong on that email; this neutral term fits both without branching per booking type.</summary>
    private static string ReferenceLine(string bookingReference) =>
        $"<p><strong>Referensnummer:</strong> {Enc(bookingReference)}</p>";

    /// <summary>Appends the reference number to a subject line so it's visible in the inbox list
    /// without opening the email (FR-92) — every template's return funnels through this.</summary>
    private static string WithReference(string subject, string bookingReference) =>
        $"{subject} ({bookingReference})";

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
