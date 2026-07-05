using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the Swedish booking-email bodies of §9 (NFR-26, NFR-32): the right content
/// per template, the conditional cancellation clause, plain-text expiry, and HTML-encoding of
/// guest-supplied text.
/// </summary>
public class BookingEmailTemplatesTests
{
    [Fact]
    public void StayRequestAcknowledgement_IsSwedish_AndGreetsGuest()
    {
        var c = BookingEmailTemplates.StayRequestAcknowledgement("Anna");
        Assert.Contains("bokningsförfrågan", c.Subject);
        Assert.Contains("Hej Anna", c.HtmlBody);
        Assert.Contains("Åkerögården", c.HtmlBody);
    }

    [Fact]
    public void GeneralInquiryAcknowledgement_DiffersFromStayRequest()
    {
        var general = BookingEmailTemplates.GeneralInquiryAcknowledgement("Anna");
        Assert.Contains("meddelande", general.Subject);
        Assert.DoesNotContain("bokningsförfrågan", general.Subject);
    }

    [Fact]
    public void AdminNewInquiry_StayRequest_ListsStayDetailsAndLink()
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingType = BookingType.StayRequest,
            ContactName = "Anna Andersson",
            ContactEmail = "anna@test.se",
            CheckIn = new DateOnly(2026, 8, 1),
            CheckOut = new DateOnly(2026, 8, 5),
            OvernightGuestCount = 12,
            MealPlan = MealPlan.FullBoardAll
        };

        var c = BookingEmailTemplates.AdminNewInquiry(booking, "https://admin.akerogarden.se/admin/bokningar/x");

        Assert.Equal("Ny bokningsförfrågan", c.Subject);
        Assert.Contains("anna@test.se", c.HtmlBody);
        Assert.Contains("Helpension", c.HtmlBody);
        Assert.Contains("https://admin.akerogarden.se/admin/bokningar/x", c.HtmlBody);
    }

    [Fact]
    public void AdminNewInquiry_GeneralInquiry_OmitsStayDetails()
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingType = BookingType.GeneralInquiry,
            ContactName = "Bo",
            ContactEmail = "bo@test.se",
            Message = "Har ni plats i sommar?"
        };

        var c = BookingEmailTemplates.AdminNewInquiry(booking, "https://admin/x");

        Assert.Equal("Ny allmän förfrågan", c.Subject);
        Assert.DoesNotContain("Måltidsplan", c.HtmlBody);
        Assert.Contains("Har ni plats i sommar?", c.HtmlBody);
    }

    [Fact]
    public void Denial_IncludesReasonAndFixedClosingLine()
    {
        var c = BookingEmailTemplates.Denial("Anna", "Fullbokat de datumen.");
        Assert.Contains("Fullbokat de datumen.", c.HtmlBody);
        Assert.Contains("Kontakta oss för att diskutera alternativa datum.", c.HtmlBody);
    }

    [Fact]
    public void Approval_IncludesMagicLinkExpiryAndDepositPolicy()
    {
        var expiry = new DateTime(2026, 7, 15, 14, 0, 0, DateTimeKind.Utc);
        var c = BookingEmailTemplates.Approval("Anna", "https://akerogarden.se/slutfor/TOKEN", expiry);

        Assert.Contains("https://akerogarden.se/slutfor/TOKEN", c.HtmlBody);
        Assert.Contains("2026", c.HtmlBody);          // exact expiry rendered in plain text
        Assert.Contains("14:00", c.HtmlBody);
        Assert.Contains("20 %", c.HtmlBody);          // deposit policy
    }

    [Fact]
    public void Cancellation_PropertyInitiated_StatesFullRefund()
    {
        var c = BookingEmailTemplates.Cancellation("Anna", CancellationInitiator.PropertyInitiated, DepositOutcome.FullRefund);
        Assert.Contains("hela handpenningen", c.HtmlBody);
    }

    [Fact]
    public void Cancellation_GuestInitiatedFullRefund_StatesRefundInFull()
    {
        var c = BookingEmailTemplates.Cancellation("Anna", CancellationInitiator.GuestInitiated, DepositOutcome.FullRefund);
        Assert.Contains("i sin helhet", c.HtmlBody);
    }

    [Fact]
    public void Cancellation_GuestInitiatedForfeited_StatesNoRefund()
    {
        var c = BookingEmailTemplates.Cancellation("Anna", CancellationInitiator.GuestInitiated, DepositOutcome.Forfeited);
        Assert.Contains("återbetalas inte", c.HtmlBody);
    }

    [Fact]
    public void TwoWeekReminder_StatesCounts_Times_AndAddress()
    {
        var c = BookingEmailTemplates.TwoWeekReminder(
            "Anna", overnightCount: 8, dayGuestCount: 3, fullBoardMealCount: 20,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 4), "Åkerögården, Norrtälje");
        Assert.Contains("Hej Anna", c.HtmlBody);
        Assert.Contains("14:00", c.HtmlBody);
        Assert.Contains("12:00", c.HtmlBody);
        Assert.Contains("övernattning):</strong> 8", c.HtmlBody);  // overnight count
        Assert.Contains("dagsgäster:</strong> 3", c.HtmlBody);     // day guest count
        Assert.Contains("Adress:", c.HtmlBody);
        Assert.Contains("Norrt", c.HtmlBody);                       // address (ä is HTML-encoded)
    }

    [Fact]
    public void CompletionNudge_CarriesFreshLinkAndExpiry()
    {
        var c = BookingEmailTemplates.CompletionNudge("Anna", "https://akerogarden.se/slutfor/tok123", new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc));
        Assert.Contains("slutfor/tok123", c.HtmlBody);
        Assert.Contains("2026", c.HtmlBody);
        Assert.Contains("inte", c.HtmlBody, StringComparison.OrdinalIgnoreCase); // "har ännu inte slutfört"
    }

    [Fact]
    public void AdminGuestNotCompleted_StatesGuestAndNewExpiry_AndLink()
    {
        var booking = new Booking
        {
            ContactName = "Anna",
            ContactEmail = "anna@test.se",
            CheckIn = new DateOnly(2026, 7, 25),
        };
        var c = BookingEmailTemplates.AdminGuestNotCompleted(booking, "https://admin.akerogarden.se/admin/bokningar/y", new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc));
        Assert.Contains("Anna", c.HtmlBody);
        Assert.Contains("admin/bokningar/y", c.HtmlBody);
        Assert.Contains("2026", c.HtmlBody);
    }

    [Fact]
    public void PreDeletionWarning_StatesDeletionDate_AndLinksToBooking()
    {
        var booking = new Booking
        {
            BookingType = BookingType.StayRequest,
            Status = BookingStatus.Completed,
            ContactName = "Anna",
            CheckIn = new DateOnly(2026, 6, 1),
            CheckOut = new DateOnly(2026, 6, 4),
        };
        var c = BookingEmailTemplates.PreDeletionWarning(booking, "https://admin.akerogarden.se/admin/bokningar/x", new DateOnly(2028, 6, 4));
        Assert.Contains("gallring", c.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2028", c.HtmlBody); // the deletion date
        Assert.Contains("admin/bokningar/x", c.HtmlBody);
    }

    [Fact]
    public void ThankYou_IsSwedish_GreetsGuest_AndInvitesReturn()
    {
        var c = BookingEmailTemplates.ThankYou("Anna");
        Assert.Contains("Hej Anna", c.HtmlBody);
        Assert.Contains("Tack", c.HtmlBody);
        Assert.Contains("välkomna tillbaka", c.HtmlBody);
    }

    [Fact]
    public void ThankYou_HtmlEncodesGuestName()
    {
        var c = BookingEmailTemplates.ThankYou("<b>Anna</b>");
        Assert.DoesNotContain("<b>Anna</b>", c.HtmlBody);
        Assert.Contains("&lt;b&gt;Anna&lt;/b&gt;", c.HtmlBody);
    }

    [Fact]
    public void GuestSuppliedText_IsHtmlEncoded()
    {
        var c = BookingEmailTemplates.Denial("<script>alert(1)</script>", "reason & more");
        Assert.DoesNotContain("<script>", c.HtmlBody);
        Assert.Contains("&lt;script&gt;", c.HtmlBody);
        Assert.Contains("reason &amp; more", c.HtmlBody);
    }
}
