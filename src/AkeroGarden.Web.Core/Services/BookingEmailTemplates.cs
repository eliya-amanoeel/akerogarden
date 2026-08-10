using System.Globalization;
using System.Net;
using System.Text;
using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

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

    /// <summary>The guest's own confirmation of what they submitted on the completion form
    /// (§9, FR-107). Deliberately <b>not</b> the same email as <see cref="AdminDetailsComplete"/>:
    /// that one is a five-line staff alert with a panel link, which tells the guest nothing about
    /// their own choices and must never be sent to them. Requires
    /// <see cref="Booking.Participants"/> and <see cref="Booking.MealSelections"/> to be loaded.
    ///
    /// <para>The closing "contact us" line is load-bearing, not politeness: the completion link is
    /// single-use (FR-62) and is already dead by the time this arrives, so without it the guest is
    /// looking at a list of their own answers with no visible way to correct one.</para></summary>
    public static EmailContent GuestDetailsReceived(
        Booking booking, IReadOnlyList<string> assignedBuildingNames, string propertyAddress)
    {
        var body = new StringBuilder();
        body.Append(Greeting(booking.ContactName));
        body.Append("<p>Tack! Vi har tagit emot era uppgifter. Här är en sammanställning av det ni har fyllt i.</p>");
        body.Append(ReferenceLine(booking.BookingReference));

        body.Append("<ul>");
        body.Append($"<li><strong>Incheckning:</strong> {FormatDate(booking.CheckIn)} kl. "
            + $"{Enc(StatedTimePolicy.Display(booking.CheckInTime, MealOccasionResolver.StandardCheckInTime))}</li>");
        body.Append($"<li><strong>Utcheckning:</strong> {FormatDate(booking.CheckOut)} kl. "
            + $"{Enc(StatedTimePolicy.Display(booking.CheckOutTime, MealOccasionResolver.StandardCheckOutTime))}</li>");
        body.Append($"<li><strong>Adress:</strong> {Enc(propertyAddress)}</li>");
        body.Append("</ul>");
        body.Append(LodgingLine(assignedBuildingNames));

        body.Append(ParticipantSection(booking));
        body.Append(MealSection(booking));

        body.Append("<p>Behöver något ändras? Kontakta oss så hjälper vi er. Länken ni fyllde i "
            + "formuläret med kan bara användas en gång och fungerar därför inte längre.</p>");

        return new EmailContent(
            WithReference("Vi har tagit emot era uppgifter", booking.BookingReference), body.ToString());
    }

    /// <summary>The participant roster as the guest entered it, with each person's allergies
    /// (§4.6) and the booking-level preference counts. Day guests carry the date they attend —
    /// without it a name in the list is unidentifiable to the person who typed it.</summary>
    private static string ParticipantSection(Booking booking)
    {
        var body = new StringBuilder();
        body.Append("<h3>Deltagare</h3>");

        if (booking.Participants.Count == 0)
        {
            body.Append("<p>Inga deltagare registrerade.</p>");
            return body.ToString();
        }

        body.Append("<ul>");
        foreach (var p in booking.Participants
            .OrderBy(p => p.ParticipantType)
            .ThenBy(p => p.FullName, StringComparer.CurrentCulture))
        {
            var line = new StringBuilder();
            line.Append($"<strong>{Enc(p.FullName)}</strong> — {Enc(BookingDisplay.ParticipantTypeLabel(p.ParticipantType))}");
            line.Append($", {Enc(BookingDisplay.AgeLabel(p.AgeCategory))}");
            if (p.AttendanceDate is { } date)
                line.Append($", {FormatDate(date)}");
            line.Append($". Allergier: {Enc(BookingDisplay.AllergySummary(p.Allergies, p.AllergyNotes))}");
            body.Append($"<li>{line}</li>");
        }
        body.Append("</ul>");

        var preferences = BookingDisplay.PreferenceSummary(booking.VegetarianCount, booking.VeganCount);
        if (preferences != "—")
            body.Append($"<p><strong>Kostönskemål:</strong> {Enc(preferences)}</p>");

        return body.ToString();
    }

    /// <summary>The group's meals day by day (FR-105). Breakfast has no dish to choose, so it is
    /// listed as attendance only; lunch and dinner name the option agreed at submission, read from
    /// the <b>snapshot</b> rather than the live meal option (NFR-15) so this email still describes
    /// what was actually chosen if the dish is renamed later.</summary>
    private static string MealSection(Booking booking)
    {
        var body = new StringBuilder();
        body.Append("<h3>Måltider</h3>");

        var occasions = MealOccasionResolver.Resolve(booking);
        if (booking.CheckIn is not { } checkIn || booking.CheckOut is not { } checkOut)
        {
            body.Append("<p>Inga måltider registrerade.</p>");
            return body.ToString();
        }

        var dishes = booking.MealSelections.ToDictionary(s => (s.Date, s.MealType), s => s.MealOptionSnapshot);
        var days = new List<string>();

        for (var date = checkIn; date <= checkOut; date = date.AddDays(1))
        {
            var served = occasions.ServedOn(date);
            if (!served.Any) continue;

            var meals = new List<string>(3);
            if (served.Breakfast) meals.Add("Frukost");
            if (served.Lunch) meals.Add(MealLine(MealType.Lunch, date, dishes));
            if (served.Dinner) meals.Add(MealLine(MealType.Dinner, date, dishes));

            days.Add($"<li><strong>{FormatDate(date)}:</strong> {string.Join(", ", meals)}</li>");
        }

        if (days.Count == 0)
        {
            // Self-catering resolves to no sittings at all — say so plainly rather than printing an
            // empty heading, which would read as data we lost rather than a choice they made.
            body.Append("<p>Ni har valt självhushåll — inga måltider är beställda.</p>");
            return body.ToString();
        }

        body.Append("<ul>");
        foreach (var day in days) body.Append(day);
        body.Append("</ul>");
        return body.ToString();
    }

    private static string MealLine(
        MealType mealType, DateOnly date, IReadOnlyDictionary<(DateOnly, MealType), string> dishes)
    {
        var label = BookingDisplay.MealTypeLabel(mealType);
        return dishes.TryGetValue((date, mealType), out var dish) && !string.IsNullOrWhiteSpace(dish)
            ? $"{Enc(label)}: {Enc(dish)}"
            : Enc(label);
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

    /// <summary>The lodging buildings the group has been given (FR-103). Omitted entirely rather
    /// than rendered empty when nothing is assigned — approval requires an assignment, so an empty
    /// set means a manually-created or otherwise unusual booking, and a blank "Ert boende:" line
    /// would read as an error to the guest.</summary>
    private static string LodgingLine(IReadOnlyList<string> buildingNames)
    {
        if (buildingNames.Count == 0) return string.Empty;
        var label = buildingNames.Count == 1 ? "Ert boende" : "Era boenden";
        return $"<p><strong>{label}:</strong> {Enc(string.Join(", ", buildingNames))}. "
            + "Ni fördelar själva gruppen mellan rummen.</p>";
    }

    /// <summary>Approval email — the assigned lodging buildings (FR-103), deposit/cancellation policy
    /// (§9, FR-52), and the booking's own check-in and check-out times.
    ///
    /// <para><b>Two variants, chosen by whether a link exists.</b> A booking approved 40 days or less
    /// before check-in carries the magic link and its exact expiry in plain text. Approved further
    /// ahead, no token is issued at all (FR-51/108) — passing null for
    /// <paramref name="magicLinkUrl"/>/<paramref name="expiryUtc"/> replaces the link paragraph with
    /// a statement that the form opens roughly six weeks before the stay and that nothing is required
    /// until then. The two are one template rather than two because everything else on the page —
    /// lodging, deposit terms, times — is identical, and a copy would drift.</para>
    ///
    /// <para><b>The booking's times, not the 14:00/12:00 standard.</b> The admin is warned about a
    /// deviating time at the approve button (FR-101) and can adjust it first (FR-48), so whatever is
    /// stored when this sends is what was agreed. Restating the standard at a guest who asked for
    /// 08:00 would be confusing without correcting anything — they would still expect 08:00 — and
    /// omitting the line entirely corrects it no better.</para></summary>
    public static EmailContent Approval(
        string contactName, string? magicLinkUrl, DateTime? expiryUtc, string bookingReference,
        IReadOnlyList<string> assignedBuildingNames, TimeOnly? checkInTime, TimeOnly? checkOutTime)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Din bokningsförfrågan är godkänd! För att slutföra din bokning behöver du fylla i uppgifter om deltagare och måltider.</p>");
        body.Append(ReferenceLine(bookingReference));
        body.Append(LodgingLine(assignedBuildingNames));
        if (magicLinkUrl is not null && expiryUtc is not null)
        {
            body.Append($"<p><a href=\"{Enc(magicLinkUrl)}\">Slutför din bokning här</a></p>");
            body.Append($"<p>Länken är giltig till och med <strong>{FormatDateTime(expiryUtc.Value)}</strong>. Fyll i uppgifterna före dess.</p>");
        }
        else
        {
            body.Append("<p>En länk för att fylla i uppgifter om deltagare och måltider skickas till er cirka sex veckor före ankomst. Fram till dess behöver ni inte göra något.</p>");
        }

        body.Append("<h3>Handpenning och avbokning</h3>");
        body.Append("<p>En handpenning på 20 % av totalsumman betalas för att bekräfta bokningen. Resterande 80 % faktureras före ankomst.</p>");
        body.Append("<p>Vid avbokning mer än två månader före ankomst återbetalas handpenningen i sin helhet. Vid avbokning två månader eller mindre före ankomst återbetalas inte handpenningen.</p>");
        body.Append($"<p>Incheckning sker kl. {Enc(StatedTimePolicy.Display(checkInTime, MealOccasionResolver.StandardCheckInTime))} "
            + $"och utcheckning kl. {Enc(StatedTimePolicy.Display(checkOutTime, MealOccasionResolver.StandardCheckOutTime))}.</p>");
        return new EmailContent(WithReference("Din bokning är godkänd – slutför dina uppgifter", bookingReference), body.ToString());
    }

    /// <summary>The completion link is now available (§9, FR-108) — sent when check-in comes within
    /// 40 days of a booking that was approved too far ahead to be given a link at the time, and by an
    /// admin resend (FR-49).
    ///
    /// <para>Deliberately <b>not</b> the approval email again: that one announces the approval and
    /// restates the deposit terms, both of which the guest already read months ago. Re-sending it
    /// would read as a second approval of the same booking.</para></summary>
    public static EmailContent CompletionLinkAvailable(
        string contactName, string magicLinkUrl, DateTime expiryUtc, string bookingReference)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Nu är det dags att fylla i uppgifter om deltagare och måltider inför er vistelse hos Åkerögården.</p>");
        body.Append(ReferenceLine(bookingReference));
        body.Append($"<p><a href=\"{Enc(magicLinkUrl)}\">Slutför din bokning här</a></p>");
        body.Append($"<p>Länken är giltig till och med <strong>{FormatDateTime(expiryUtc)}</strong>. Fyll i uppgifterna före dess.</p>");
        body.Append("<p>Ha gärna namn på alla deltagare och eventuella allergier till hands när ni fyller i.</p>");
        return new EmailContent(WithReference("Nu kan ni fylla i era uppgifter", bookingReference), body.ToString());
    }

    /// <summary>Standard 14-day reminder to the guest (§9, FR-64) — participant/day-guest counts,
    /// meal summary, the assigned lodging building(s), <b>the booking's own check-in and check-out
    /// times</b>, and the property address. Sent when the booking is <c>DetailsComplete</c> or
    /// later.
    ///
    /// <para>The times are the booking's, not the 14:00/12:00 standard (FR-64): by this point the
    /// guest has stated a time and the admin has had every chance to adjust it (FR-48/101), so
    /// restating the default would contradict what was agreed. A booking that never stated one
    /// falls back to the standard.</para></summary>
    public static EmailContent TwoWeekReminder(
        string contactName,
        int overnightCount,
        int dayGuestCount,
        int groupMealCount,
        DateOnly? checkIn,
        DateOnly? checkOut,
        TimeOnly? checkInTime,
        TimeOnly? checkOutTime,
        string propertyAddress,
        string bookingReference,
        IReadOnlyList<string> assignedBuildingNames)
    {
        var body = new StringBuilder();
        body.Append(Greeting(contactName));
        body.Append("<p>Snart är det dags för er vistelse hos Åkerögården! Här är en påminnelse med era uppgifter.</p>");
        body.Append("<ul>");
        body.Append($"<li><strong>Referensnummer:</strong> {Enc(bookingReference)}</li>");
        body.Append($"<li><strong>Incheckning:</strong> {FormatDate(checkIn)} kl. {Enc(StatedTimePolicy.Display(checkInTime, MealOccasionResolver.StandardCheckInTime))}</li>");
        body.Append($"<li><strong>Utcheckning:</strong> {FormatDate(checkOut)} kl. {Enc(StatedTimePolicy.Display(checkOutTime, MealOccasionResolver.StandardCheckOutTime))}</li>");
        body.Append($"<li><strong>Antal deltagare (övernattning):</strong> {overnightCount.ToString(Sv)}</li>");
        body.Append($"<li><strong>Antal dagsgäster:</strong> {dayGuestCount.ToString(Sv)}</li>");
        // The group eats one dish per meal (FR-105), so this counts *meals*, not portions. Labelled
        // accordingly: "Antal beställda måltider: 3" for a party of 24 would read as a mistake.
        body.Append($"<li><strong>Antal måltider för gruppen:</strong> {groupMealCount.ToString(Sv)}</li>");
        if (assignedBuildingNames.Count > 0)
            body.Append($"<li><strong>Ert boende:</strong> {Enc(string.Join(", ", assignedBuildingNames))}</li>");
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
        _ => "—"
    };
}
