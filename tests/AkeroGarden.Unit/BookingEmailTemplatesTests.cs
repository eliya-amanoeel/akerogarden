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
    public void Approval_IncludesMagicLinkExpiryAndDepositPolicy()
    {
        var expiry = new DateTime(2026, 7, 15, 14, 0, 0, DateTimeKind.Utc);
        var c = BookingEmailTemplates.Approval(
            "Anna", "https://akerogarden.se/slutfor/TOKEN", expiry, Ref, ["Smedjan"],
            new TimeOnly(14, 0), new TimeOnly(12, 0));

        Assert.Contains("https://akerogarden.se/slutfor/TOKEN", c.HtmlBody);
        Assert.Contains("Smedjan", c.HtmlBody);       // assigned lodging stated (FR-103)
        Assert.Contains("2026", c.HtmlBody);          // exact expiry rendered in plain text
        Assert.Contains("14:00", c.HtmlBody);
        Assert.Contains("20 %", c.HtmlBody);          // deposit policy
        Assert.Contains(Ref, c.HtmlBody);
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
            new TimeOnly(8, 0), new TimeOnly(18, 30));

        Assert.Contains("Incheckning sker kl. 08:00", c.HtmlBody);
        Assert.Contains("utcheckning kl. 18:30", c.HtmlBody);
    }

    [Fact]
    public void Approval_FallsBackToTheStandardTimes_WhenNoneWereStated()
    {
        var c = BookingEmailTemplates.Approval(
            "Anna", "https://akerogarden.se/slutfor/TOKEN",
            new DateTime(2026, 7, 15, 14, 0, 0, DateTimeKind.Utc), Ref, [], null, null);

        Assert.Contains("Incheckning sker kl. 14:00", c.HtmlBody);
        Assert.Contains("utcheckning kl. 12:00", c.HtmlBody);
    }

    [Fact]
    public void Cancellation_PropertyInitiated_StatesFullRefund()
    {
        var c = BookingEmailTemplates.Cancellation("Anna", CancellationInitiator.PropertyInitiated, DepositOutcome.FullRefund, Ref);
        Assert.Contains("hela handpenningen", c.HtmlBody);
        Assert.Contains(Ref, c.HtmlBody);
    }

    [Fact]
    public void Cancellation_GuestInitiatedFullRefund_StatesRefundInFull()
    {
        var c = BookingEmailTemplates.Cancellation("Anna", CancellationInitiator.GuestInitiated, DepositOutcome.FullRefund, Ref);
        Assert.Contains("i sin helhet", c.HtmlBody);
    }

    [Fact]
    public void Cancellation_GuestInitiatedForfeited_StatesNoRefund()
    {
        var c = BookingEmailTemplates.Cancellation("Anna", CancellationInitiator.GuestInitiated, DepositOutcome.Forfeited, Ref);
        Assert.Contains("återbetalas inte", c.HtmlBody);
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
        Assert.Contains("2026", c.HtmlBody);
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
        Assert.Contains("2026", c.HtmlBody);
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
}
