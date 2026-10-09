using System.Net;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the Swedish booking-email bodies of §9 (NFR-26, NFR-32): the right content
/// per template, the conditional cancellation clause, plain-text expiry, and HTML-encoding of
/// guest-supplied text.
/// </summary>
public class BookingEmailTemplatesTests
{
    private const string Ref = "AG-2026-0042";
    private const string TermsUrl = "https://akerogarden.se/bokningsvillkor";

    [Fact]
    public void StayRequestAcknowledgement_IsSwedish_AndGreetsGuest()
    {
        var c = BookingEmailTemplates.StayRequestAcknowledgement("Anna", Ref);
        Assert.Contains("bokningsförfrågan", c.Subject);
        Assert.Contains("Hej Anna", c.HtmlBody);
        Assert.Contains("Åkerögården", c.HtmlBody);
        Assert.Contains(Ref, c.HtmlBody);
    }

    [Fact]
    public void GeneralInquiryAcknowledgement_DiffersFromStayRequest()
    {
        var general = BookingEmailTemplates.GeneralInquiryAcknowledgement("Anna", Ref);
        Assert.Contains("meddelande", general.Subject);
        Assert.DoesNotContain("bokningsförfrågan", general.Subject);
        Assert.Contains(Ref, general.HtmlBody);
    }

    [Fact]
    public void AdminNewInquiry_StayRequest_ListsStayDetailsAndLink()
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = Ref,
            BookingType = BookingType.StayRequest,
            ContactName = "Anna Andersson",
            ContactEmail = "anna@test.se",
            CheckIn = new DateOnly(2026, 8, 1),
            CheckOut = new DateOnly(2026, 8, 5),
            OvernightGuestCount = 12,
            MealPlan = MealPlan.FullBoardAll
        };

        var c = BookingEmailTemplates.AdminNewInquiry(booking, "https://admin.akerogarden.se/bokningar/x");

        Assert.Equal($"Ny bokningsförfrågan ({Ref})", c.Subject);
        Assert.Contains("anna@test.se", c.HtmlBody);
        Assert.Contains("Helpension", c.HtmlBody);
        Assert.Contains("https://admin.akerogarden.se/bokningar/x", c.HtmlBody);
        Assert.Contains(Ref, c.HtmlBody);
    }

    [Fact]
    public void AdminNewInquiry_GeneralInquiry_OmitsStayDetails()
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = Ref,
            BookingType = BookingType.GeneralInquiry,
            ContactName = "Bo",
            ContactEmail = "bo@test.se",
            Message = "Har ni plats i sommar?"
        };

        var c = BookingEmailTemplates.AdminNewInquiry(booking, "https://admin/x");

        Assert.Equal($"Ny allmän förfrågan ({Ref})", c.Subject);
        Assert.DoesNotContain("Måltidsplan", c.HtmlBody);
        Assert.Contains("Har ni plats i sommar?", c.HtmlBody);
    }

    [Fact]
    public void Denial_IncludesReasonAndFixedClosingLine()
    {
        var c = BookingEmailTemplates.Denial("Anna", "Fullbokat de datumen.", Ref);
        Assert.Contains("Fullbokat de datumen.", c.HtmlBody);
        Assert.Contains("Kontakta oss för att diskutera alternativa datum.", c.HtmlBody);
        Assert.Contains(Ref, c.HtmlBody);
    }

    [Fact]
    public void Approval_IncludesMagicLinkExpiry_AndPointsToTheTermsInsteadOfRestatingThem()
    {
        var expiry = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc); // 14:00 Swedish summer time
        var c = BookingEmailTemplates.Approval(
            "Anna", "https://akerogarden.se/slutfor/TOKEN", expiry, Ref, ["Smedjan"],
            new TimeOnly(14, 0), new TimeOnly(12, 0), TermsUrl, depositRequested: false);

        Assert.Contains("https://akerogarden.se/slutfor/TOKEN", c.HtmlBody);
        Assert.Contains("Smedjan", c.HtmlBody);       // assigned lodging stated (FR-103)
        // Exact expiry in plain text, in Swedish time (§2.6). The whole phrase, because "14:00" alone
        // also matches the check-in time and "2026" matches anything — which is how a UTC expiry
        // went out in every email for a month without a test noticing.
        Assert.Contains("giltig till och med <strong>15 juli 2026 kl. 14:00</strong>", c.HtmlBody);
        Assert.Contains(Ref, c.HtmlBody);

        // FR-79/§5.6: no cancellation scale and no balance due date — the old 20 % / two-month rule
        // contradicted the client's adopted terms, and the email now points to them instead.
        Assert.DoesNotContain("två månader", c.HtmlBody);
        Assert.DoesNotContain("80 %", c.HtmlBody);
        Assert.DoesNotContain("före ankomst", c.HtmlBody);
        Assert.Contains("de boknings- och vistelsevillkor som gällde när bokningen bekräftades", c.HtmlBody);
        Assert.Contains("Pris och betalningsvillkor får ni från oss separat", c.HtmlBody); // direct path

        // Under §2 of the client's terms the booking binds when it is confirmed and accepted, so the
        // full terms must be reachable from this email — by their published name.
        Assert.Contains(TermsUrl, c.HtmlBody);
        Assert.Contains("boknings- och vistelsevillkor</a>", c.HtmlBody);
        Assert.DoesNotContain("ordningsvillkor", c.HtmlBody);
    }

    [Fact]
    public void Approval_AfterADeposit_ConfirmsWithoutClaimingTheDepositArrived()
    {
        // FR-115: the guest was already told yes in the deposit request, so this confirms. It must not
        // say the deposit has arrived — the admin may confirm without payment, and the email must
        // never assert what the system cannot know.
        var c = BookingEmailTemplates.Approval(
            "Anna", magicLinkUrl: null, expiryUtc: null, Ref, ["Smedjan"],
            new TimeOnly(14, 0), new TimeOnly(12, 0), TermsUrl, depositRequested: true);

        Assert.Contains("Er bokning är nu bekräftad", c.HtmlBody);
        Assert.Contains("bekräftad", c.Subject);
        Assert.DoesNotContain("godkänd", c.HtmlBody);
        Assert.DoesNotContain("kommit in", c.HtmlBody);
        Assert.Contains("Resterande belopp faktureras av oss", c.HtmlBody);
        Assert.Contains(TermsUrl, c.HtmlBody);
    }

    [Fact]
    public void Approval_StatesTheBookingsOwnTimes_NotTheStandard()
    {
        // A guest who asked to arrive at 08:00 must not be told check-in is at 14:00. The admin is
        // warned about the deviation at the approve button (FR-101) and can adjust the time first
        // (FR-48), so what is stored when this sends is what was agreed.
        var c = BookingEmailTemplates.Approval(
            "Anna", "https://akerogarden.se/slutfor/TOKEN",
            new DateTime(2026, 7, 15, 14, 0, 0, DateTimeKind.Utc), Ref, [],
            new TimeOnly(8, 0), new TimeOnly(18, 30), TermsUrl, depositRequested: false);

        Assert.Contains("Incheckning sker kl. 08:00", c.HtmlBody);
        Assert.Contains("utcheckning kl. 18:30", c.HtmlBody);
    }

    [Fact]
    public void Approval_FallsBackToTheStandardTimes_WhenNoneWereStated()
    {
        var c = BookingEmailTemplates.Approval(
            "Anna", "https://akerogarden.se/slutfor/TOKEN",
            new DateTime(2026, 7, 15, 14, 0, 0, DateTimeKind.Utc), Ref, [], null, null, TermsUrl, depositRequested: false);

        Assert.Contains("Incheckning sker kl. 14:00", c.HtmlBody);
        Assert.Contains("utcheckning kl. 12:00", c.HtmlBody);
    }

    [Fact]
    public void Approval_BeyondTheIssueWindow_CarriesNoLinkAndPromisesOneLater()
    {
        // FR-52/108: a booking approved more than 40 days out has no token, so the email must not
        // show a link or an expiry — it tells the guest nothing is required of them yet.
        var c = BookingEmailTemplates.Approval(
            "Anna", magicLinkUrl: null, expiryUtc: null, Ref, ["Smedjan"],
            new TimeOnly(14, 0), new TimeOnly(12, 0), TermsUrl, depositRequested: false);

        Assert.DoesNotContain("/slutfor/", c.HtmlBody);
        Assert.DoesNotContain("Länken är giltig", c.HtmlBody);
        Assert.Contains("sex veckor före ankomst", c.HtmlBody);
        Assert.Contains("behöver ni inte göra något", c.HtmlBody);

        // Everything that does not depend on the link is unchanged.
        Assert.Contains("Smedjan", c.HtmlBody);
        Assert.Contains("Betalning och avbokning", c.HtmlBody);
        Assert.Contains(Ref, c.HtmlBody);
        // The booking is binding regardless of whether the completion link was issued yet, so the
        // terms link is not part of the link variant.
        Assert.Contains(TermsUrl, c.HtmlBody);
    }

    [Fact]
    public void CompletionLinkAvailable_CarriesTheLinkExpiryAndReference()
    {
        var expiry = new DateTime(2026, 12, 7, 13, 0, 0, DateTimeKind.Utc); // 14:00 Swedish winter time
        var c = BookingEmailTemplates.CompletionLinkAvailable(
            "Anna", "https://akerogarden.se/slutfor/TOKEN", expiry, Ref);

        Assert.Contains("https://akerogarden.se/slutfor/TOKEN", c.HtmlBody);
        Assert.Contains("giltig till och med <strong>7 december 2026 kl. 14:00</strong>", c.HtmlBody); // §2.6
        Assert.Contains(Ref, c.HtmlBody);
        Assert.Contains(Ref, c.Subject);

        // Not a second approval: the payment and cancellation lines belong to the approval email only.
        Assert.DoesNotContain("Betalning och avbokning", c.HtmlBody);
        Assert.DoesNotContain("godkänd", c.HtmlBody);
    }

    private static readonly PaymentDetails Payment =
        new("Kaldeiska katolska Mar Narsay Missionen i Södertälje", "5713-3399", "123 583 24 23", "5364", "108 72 28");

    [Fact]
    public void Cancellation_PropertyInitiated_RefundsEverythingPaid()
    {
        var c = BookingEmailTemplates.Cancellation("Anna", CancellationNotice.PropertyCancelled, depositPaid: true, Ref, TermsUrl);
        Assert.Contains("Vi återbetalar allt ni har betalat till oss.", c.HtmlBody);
        Assert.DoesNotContain("återbetalas inte", c.HtmlBody);
        Assert.Contains(Ref, c.HtmlBody);
    }

    [Fact]
    public void Cancellation_GuestInitiated_DepositPaid_KeepsTheDeposit_AndPointsToTheTermsForTheRest()
    {
        var c = BookingEmailTemplates.Cancellation("Anna", CancellationNotice.GuestCancelled, depositPaid: true, Ref, TermsUrl);
        Assert.Contains("Handpenningen återbetalas inte.", c.HtmlBody);
        Assert.Contains("Eventuell ytterligare avbokningsavgift", c.HtmlBody);
        Assert.Contains("som gällde när er bokning bekräftades", c.HtmlBody); // the client's §32
        Assert.Contains(TermsUrl, c.HtmlBody);
        // Never the scale itself (FR-79).
        Assert.DoesNotContain("veckor", c.HtmlBody);
        Assert.DoesNotContain("%", c.HtmlBody);
    }

    [Fact]
    public void Cancellation_GuestInitiated_NoDeposit_SaysNothingAboutADeposit()
    {
        // A booking approved directly was never asked for a deposit; mentioning one would be false.
        var c = BookingEmailTemplates.Cancellation("Anna", CancellationNotice.GuestCancelled, depositPaid: false, Ref, TermsUrl);
        Assert.DoesNotContain("Handpenning", c.HtmlBody);
        Assert.Contains("Eventuell avbokningsavgift följer", c.HtmlBody);
        Assert.Contains(TermsUrl, c.HtmlBody);
    }

    [Fact]
    public void Cancellation_NeverConfirmed_SaysNothingIsOwed()
    {
        var c = BookingEmailTemplates.Cancellation("Anna", CancellationNotice.NothingOwed, depositPaid: false, Ref, TermsUrl);
        Assert.Contains("Bokningen hade ännu inte bekräftats", c.HtmlBody);
        Assert.Contains("inte skyldiga oss något", c.HtmlBody);
        Assert.DoesNotContain("avbokningsavgift", c.HtmlBody);
    }

    [Fact]
    public void DepositRequest_StatesEverythingTheGuestNeedsToPay_AndThatItIsNotRefundable()
    {
        var c = BookingEmailTemplates.DepositRequest(
            "Anna", Ref, new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 3), ["Smedjan"],
            new DateOnly(2026, 10, 16), Payment, TermsUrl);

        Assert.Contains(Ref, c.Subject);
        Assert.Contains("1 maj 2027 – 3 maj 2027", c.HtmlBody);          // the dates held
        Assert.Contains("Smedjan", c.HtmlBody);
        Assert.Contains("20 %", c.HtmlBody);
        Assert.Contains("separat mejl", c.HtmlBody);                     // amount comes from the client (C8)
        Assert.Contains("<strong>16 oktober 2026</strong>", c.HtmlBody); // due date
        // Decoded: the body HTML-encodes text, and å/ä/ö come out as numeric entities.
        Assert.Contains("Kaldeiska katolska Mar Narsay Missionen i Södertälje", WebUtility.HtmlDecode(c.HtmlBody));
        Assert.Contains("5713-3399", c.HtmlBody);
        Assert.Contains("123 583 24 23", c.HtmlBody);
        Assert.Contains("clearingnummer 5364, kontonummer 108 72 28", c.HtmlBody);
        Assert.Contains($"referensnumret <strong>{Ref}</strong> som meddelande", c.HtmlBody);
        // Must be shown before the guest pays, or it does not bind a consumer.
        Assert.Contains("handpenningen återbetalas inte vid avbokning", c.HtmlBody);
        Assert.Contains(TermsUrl, c.HtmlBody);
        Assert.DoesNotContain("/slutfor/", c.HtmlBody);                  // no link before confirmation
    }

    [Fact]
    public void DepositReminder_RepeatsWhereToPay_AndCitesTheTermsSection()
    {
        var c = BookingEmailTemplates.DepositReminder("Anna", Ref, new DateOnly(2026, 10, 16), Payment, TermsUrl);

        Assert.Contains(Ref, c.Subject);
        Assert.Contains("16 oktober 2026", c.HtmlBody);
        Assert.Contains("5713-3399", c.HtmlBody);
        Assert.Contains("punkt 6", c.HtmlBody);
        Assert.Contains(TermsUrl, c.HtmlBody);
        Assert.Contains("redan betalat", c.HtmlBody);
    }

    [Fact]
    public void TwoWeekReminder_StatesCounts_Times_AndAddress()
    {
        var c = BookingEmailTemplates.TwoWeekReminder(
            "Anna", overnightCount: 8, dayGuestCount: 3, groupMealCount: 5,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 4),
            new TimeOnly(14, 0), new TimeOnly(12, 0), "Åkerögården, Norrtälje", Ref,
            assignedBuildingNames: ["Smedjan", "Tallstugan"]);
        Assert.Contains("Hej Anna", c.HtmlBody);
        Assert.Contains("14:00", c.HtmlBody);
        Assert.Contains("12:00", c.HtmlBody);
        Assert.Contains("övernattning):</strong> 8", c.HtmlBody);  // overnight count
        Assert.Contains("dagsgäster:</strong> 3", c.HtmlBody);     // day guest count
        Assert.Contains("Adress:", c.HtmlBody);
        Assert.Contains("Norrt", c.HtmlBody);                       // address (ä is HTML-encoded)
        Assert.Contains(Ref, c.HtmlBody);
        // The assigned lodging the group needs in writing to divide itself up (FR-64/FR-103).
        Assert.Contains("Ert boende:", c.HtmlBody);
        Assert.Contains("Smedjan, Tallstugan", c.HtmlBody);
    }

    [Fact]
    public void TwoWeekReminder_StatesTheBookingsOwnTimes_NotTheStandard()
    {
        // FR-64: by now the guest has stated a time and the admin has had every chance to adjust it,
        // so restating 14:00/12:00 would contradict what was agreed.
        var c = BookingEmailTemplates.TwoWeekReminder(
            "Anna", overnightCount: 8, dayGuestCount: 0, groupMealCount: 5,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 4),
            new TimeOnly(8, 30), new TimeOnly(18, 0), "Åkerögården, Norrtälje", Ref,
            assignedBuildingNames: []);

        Assert.Contains("kl. 08:30", c.HtmlBody);
        Assert.Contains("kl. 18:00", c.HtmlBody);
        Assert.DoesNotContain("14:00", c.HtmlBody);
        Assert.DoesNotContain("12:00", c.HtmlBody);
    }

    [Fact]
    public void TwoWeekReminder_FallsBackToTheStandardTimes_WhenNoneWereStated()
    {
        var c = BookingEmailTemplates.TwoWeekReminder(
            "Anna", overnightCount: 8, dayGuestCount: 0, groupMealCount: 5,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 4),
            null, null, "Åkerögården, Norrtälje", Ref,
            assignedBuildingNames: []);

        Assert.Contains("kl. 14:00", c.HtmlBody);
        Assert.Contains("kl. 12:00", c.HtmlBody);
    }

    [Fact]
    public void TwoWeekReminder_LabelsTheMealCountAsTheGroups_NotAsPortions()
    {
        // The group eats one dish per meal (FR-105), so 5 means five meals, not five portions.
        // "Antal beställda måltider: 5" for a party of 24 would read as a mistake.
        var c = BookingEmailTemplates.TwoWeekReminder(
            "Anna", overnightCount: 24, dayGuestCount: 0, groupMealCount: 5,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 4),
            null, null, "Åkerögården, Norrtälje", Ref,
            assignedBuildingNames: []);

        Assert.Contains("Antal måltider för gruppen:</strong> 5", c.HtmlBody);
    }

    [Fact]
    public void TwoWeekReminder_WithNoAssignedBuildings_OmitsTheLodgingLineEntirely()
    {
        // A blank "Ert boende:" would read to the guest as a mistake rather than as absence.
        var c = BookingEmailTemplates.TwoWeekReminder(
            "Anna", overnightCount: 8, dayGuestCount: 3, groupMealCount: 5,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 4),
            new TimeOnly(14, 0), new TimeOnly(12, 0), "Åkerögården, Norrtälje", Ref,
            assignedBuildingNames: []);
        Assert.DoesNotContain("Ert boende", c.HtmlBody);
    }

    [Fact]
    public void CompletionNudge_CarriesFreshLinkAndExpiry()
    {
        var c = BookingEmailTemplates.CompletionNudge("Anna", "https://akerogarden.se/slutfor/tok123", new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc), Ref);
        Assert.Contains("slutfor/tok123", c.HtmlBody);
        Assert.Contains("20 juli 2026 kl. 12:00", c.HtmlBody); // 10:00 UTC in Swedish summer time
        Assert.Contains("inte", c.HtmlBody, StringComparison.OrdinalIgnoreCase); // "har ännu inte slutfört"
        Assert.Contains(Ref, c.HtmlBody);
    }

    [Fact]
    public void AdminGuestNotCompleted_StatesGuestAndNewExpiry_AndLink()
    {
        var booking = new Booking
        {
            BookingReference = Ref,
            ContactName = "Anna",
            ContactEmail = "anna@test.se",
            CheckIn = new DateOnly(2026, 7, 25),
        };
        var c = BookingEmailTemplates.AdminGuestNotCompleted(booking, "https://admin.akerogarden.se/bokningar/y", new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc));
        Assert.Contains("Anna", c.HtmlBody);
        Assert.Contains("bokningar/y", c.HtmlBody);
        Assert.Contains("20 juli 2026 kl. 12:00", c.HtmlBody); // 10:00 UTC in Swedish summer time
        Assert.Contains(Ref, c.HtmlBody);
    }

    [Fact]
    public void PreDeletionWarning_StatesDeletionDate_AndLinksToBooking()
    {
        var booking = new Booking
        {
            BookingReference = Ref,
            BookingType = BookingType.StayRequest,
            Status = BookingStatus.Completed,
            ContactName = "Anna",
            CheckIn = new DateOnly(2026, 6, 1),
            CheckOut = new DateOnly(2026, 6, 4),
        };
        var c = BookingEmailTemplates.PreDeletionWarning(booking, "https://admin.akerogarden.se/bokningar/x", new DateOnly(2028, 6, 4));
        Assert.Contains("gallring", c.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2028", c.HtmlBody); // the deletion date
        Assert.Contains("bokningar/x", c.HtmlBody);
        Assert.Contains(Ref, c.HtmlBody);
    }

    [Fact]
    public void ThankYou_IsSwedish_GreetsGuest_AndInvitesReturn()
    {
        var c = BookingEmailTemplates.ThankYou("Anna", Ref);
        Assert.Contains("Hej Anna", c.HtmlBody);
        Assert.Contains("Tack", c.HtmlBody);
        Assert.Contains("välkomna tillbaka", c.HtmlBody);
        Assert.Contains(Ref, c.HtmlBody);
    }

    [Fact]
    public void ThankYou_HtmlEncodesGuestName()
    {
        var c = BookingEmailTemplates.ThankYou("<b>Anna</b>", Ref);
        Assert.DoesNotContain("<b>Anna</b>", c.HtmlBody);
        Assert.Contains("&lt;b&gt;Anna&lt;/b&gt;", c.HtmlBody);
    }

    [Fact]
    public void GuestSuppliedText_IsHtmlEncoded()
    {
        var c = BookingEmailTemplates.Denial("<script>alert(1)</script>", "reason & more", Ref);
        Assert.DoesNotContain("<script>", c.HtmlBody);
        Assert.Contains("&lt;script&gt;", c.HtmlBody);
        Assert.Contains("reason &amp; more", c.HtmlBody);
    }

    // ── FR-107: the guest's own confirmation of what they submitted ────────────────

    /// <summary>Every guest- or admin-supplied value is HTML-encoded into the body, and
    /// <see cref="WebUtility.HtmlEncode"/> renders each non-ASCII character as a numeric entity —
    /// "Köttbullar" arrives as "K&amp;#246;ttbullar". Assertions on those values must encode the
    /// same way. The older tests in this file assert an ASCII prefix instead ("Norrt"), which
    /// quietly stops checking at the first å/ä/ö and would pass on a truncated value.</summary>
    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    /// <summary>A catered 2-night booking on the standard edge schedule (arrival dinner, departure
    /// breakfast) with one overnight guest, one day guest, and a dish chosen for every sitting the
    /// group actually takes: 10 Aug dinner, 11 Aug lunch + dinner.</summary>
    private static Booking CateredBooking() => new()
    {
        Id = Guid.NewGuid(),
        BookingReference = Ref,
        ContactName = "Anna",
        ContactEmail = "anna@test.se",
        BookingType = BookingType.StayRequest,
        MealPlan = MealPlan.FullBoardAll,
        CheckIn = new DateOnly(2026, 8, 10),
        CheckOut = new DateOnly(2026, 8, 12),
        CheckInTime = new TimeOnly(16, 30),
        CheckOutTime = new TimeOnly(10, 0),
        ArrivalDinner = true,
        DepartureBreakfast = true,
        DayGuestLunch = true,
        VegetarianCount = 2,
        Participants =
        [
            new Participant
            {
                FullName = "Bengt Bok",
                ParticipantType = ParticipantType.Overnight,
                AgeCategory = AgeCategory.Adult,
                Allergies = [Allergy.Gluten],
            },
            new Participant
            {
                FullName = "Cecilia Dag",
                ParticipantType = ParticipantType.DayGuest,
                AgeCategory = AgeCategory.Child,
                AttendanceDate = new DateOnly(2026, 8, 11),
                Allergies = [Allergy.Other],
                AllergyNotes = "selleri",
            },
        ],
        MealSelections =
        [
            new BookingMealSelection { Date = new DateOnly(2026, 8, 10), MealType = MealType.Dinner, MealOptionSnapshot = "Köttbullar" },
            new BookingMealSelection { Date = new DateOnly(2026, 8, 11), MealType = MealType.Lunch, MealOptionSnapshot = "Ärtsoppa" },
            new BookingMealSelection { Date = new DateOnly(2026, 8, 11), MealType = MealType.Dinner, MealOptionSnapshot = "Laxfilé" },
        ],
    };

    [Fact]
    public void GuestDetailsReceived_RepeatsTheStayTheParticipantsAndTheMealsBack()
    {
        var c = BookingEmailTemplates.GuestDetailsReceived(
            CateredBooking(), ["Smedjan"], "Åkerögården, Norrtälje");

        Assert.Contains("Hej Anna", c.HtmlBody);
        Assert.Contains(Ref, c.HtmlBody);
        Assert.Contains(Ref, c.Subject);

        // The booking's own stated times, not the 14:00/12:00 standard (§2.8).
        Assert.Contains("kl. 16:30", c.HtmlBody);
        Assert.Contains("kl. 10:00", c.HtmlBody);
        Assert.DoesNotContain("14:00", c.HtmlBody);

        Assert.Contains("Ert boende:", c.HtmlBody);
        Assert.Contains("Smedjan", c.HtmlBody);
        Assert.Contains(Enc("Norrtälje"), c.HtmlBody);

        // Every participant, with their own allergies — including the free-text note behind "Annat".
        Assert.Contains(Enc("Bengt Bok"), c.HtmlBody);
        Assert.Contains(Enc("Glutenintolerans"), c.HtmlBody);
        Assert.Contains(Enc("Cecilia Dag"), c.HtmlBody);
        Assert.Contains(Enc("selleri"), c.HtmlBody);
        Assert.Contains("Kostönskemål:", c.HtmlBody); // literal label, not encoded
        Assert.Contains(Enc("2 vegetariska"), c.HtmlBody);

        // Every sitting the group takes, with the dish agreed for it.
        Assert.Contains(Enc("Köttbullar"), c.HtmlBody);
        Assert.Contains(Enc("Ärtsoppa"), c.HtmlBody);
        Assert.Contains(Enc("Laxfilé"), c.HtmlBody);
        Assert.Contains("Frukost", c.HtmlBody); // departure-day breakfast, no dish to choose

        // The line that makes the email actionable — the link is dead by now (FR-62/107).
        Assert.Contains("Kontakta oss", c.HtmlBody);
    }

    [Fact]
    public void GuestDetailsReceived_ShowsOnlyTheSittingsTheGroupTakes()
    {
        // The standard edge schedule means no lunch on the arrival day and no dinner on the
        // departure day. A grid that printed every possible sitting would tell the guest they had
        // ordered meals they did not.
        var c = BookingEmailTemplates.GuestDetailsReceived(
            CateredBooking(), ["Smedjan"], "Åkerögården, Norrtälje");

        var arrivalDay = SectionForDay(c.HtmlBody, "10 augusti 2026");
        Assert.Contains("Middag", arrivalDay);
        Assert.DoesNotContain("Lunch", arrivalDay);
        Assert.DoesNotContain("Frukost", arrivalDay);

        var departureDay = SectionForDay(c.HtmlBody, "12 augusti 2026");
        Assert.Contains("Frukost", departureDay);
        Assert.DoesNotContain("Middag", departureDay);
    }

    /// <summary>The one <c>&lt;li&gt;</c> of the <b>meal</b> list belonging to <paramref name="day"/>,
    /// so a "no lunch on the arrival day" assertion cannot pass or fail on another day's text.
    /// Anchored past the Måltider heading deliberately: the same dates appear earlier in the
    /// check-in/check-out lines, and matching those would have made these assertions meaningless
    /// while still reading as green.</summary>
    private static string SectionForDay(string html, string day)
    {
        var mealsAt = html.IndexOf("<h3>Måltider</h3>", StringComparison.Ordinal);
        Assert.True(mealsAt >= 0, "Måltidsavsnittet saknas i mejlet.");

        var start = html.IndexOf(day, mealsAt, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Dagen {day} saknas i måltidslistan.");

        var end = html.IndexOf("</li>", start, StringComparison.Ordinal);
        return html[start..(end < 0 ? html.Length : end)];
    }

    [Fact]
    public void GuestDetailsReceived_SelfCatering_SaysSo_RatherThanPrintingAnEmptyMealList()
    {
        var booking = CateredBooking();
        booking.MealPlan = MealPlan.SelfCateringAll;
        booking.ArrivalDinner = false;
        booking.DepartureBreakfast = false;
        booking.MealSelections.Clear();

        var c = BookingEmailTemplates.GuestDetailsReceived(booking, ["Smedjan"], "Åkerögården, Norrtälje");

        Assert.Contains("självhushåll", c.HtmlBody);
        Assert.DoesNotContain("Middag", c.HtmlBody);
    }

    [Fact]
    public void GuestDetailsReceived_CarriesNoAdminLink()
    {
        // FR-107 exists because the admin notification (FR-61) is not a copy of the guest's choices.
        // Sending the guest an admin-panel URL would be the shortcut this requirement rules out.
        var c = BookingEmailTemplates.GuestDetailsReceived(
            CateredBooking(), ["Smedjan"], "Åkerögården, Norrtälje");

        Assert.DoesNotContain("admin", c.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adminpanelen", c.HtmlBody);
    }

    [Fact]
    public void GuestDetailsReceived_HtmlEncodesParticipantNamesAndAllergyNotes()
    {
        var booking = CateredBooking();
        booking.Participants[0].FullName = "<b>Bengt</b>";
        booking.Participants[1].AllergyNotes = "selleri & nötter";

        var c = BookingEmailTemplates.GuestDetailsReceived(booking, [], "Åkerögården");

        Assert.DoesNotContain("<b>Bengt</b>", c.HtmlBody);
        Assert.Contains("&lt;b&gt;Bengt&lt;/b&gt;", c.HtmlBody);
        Assert.Contains(Enc("selleri & nötter"), c.HtmlBody);
    }
}
