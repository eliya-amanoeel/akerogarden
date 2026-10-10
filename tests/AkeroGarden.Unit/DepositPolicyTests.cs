using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the deposit rules (FR-115/116): the due date and its two clamps, the overdue
/// boundary, and when the single reminder is due. Pure — today is passed in.
/// </summary>
public class DepositPolicyTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);

    [Fact]
    public void DueDate_IsSevenDaysAhead_WhenArrivalIsFarOff()
    {
        Assert.Equal(new DateOnly(2026, 10, 16), DepositPolicy.DueDate(Today, checkIn: new(2027, 5, 1)));
    }

    [Fact]
    public void DueDate_IsTheDayBeforeArrival_WhenSevenDaysWouldPassIt()
    {
        // Check-in in 5 days: 7 days ahead would land after arrival.
        Assert.Equal(new DateOnly(2026, 10, 13), DepositPolicy.DueDate(Today, checkIn: new(2026, 10, 14)));
    }

    [Fact]
    public void DueDate_AtTheEdge_SevenDaysIsExactlyTheDayBeforeArrival()
    {
        Assert.Equal(new DateOnly(2026, 10, 16), DepositPolicy.DueDate(Today, checkIn: new(2026, 10, 17)));
    }

    [Theory]
    [InlineData(2026, 10, 10)] // arriving tomorrow — the day before arrival is today
    [InlineData(2026, 10, 9)]  // arriving today
    [InlineData(2026, 9, 30)]  // a stay recorded after the fact (FR-112)
    public void DueDate_IsNeverBeforeToday(int year, int month, int day)
    {
        Assert.Equal(Today, DepositPolicy.DueDate(Today, checkIn: new(year, month, day)));
    }

    [Fact]
    public void IsOverdue_OnlyAfterTheDueDate_TheDueDateItselfIsStillADayToPay()
    {
        var due = new DateOnly(2026, 10, 16);
        Assert.False(DepositPolicy.IsOverdue(due, today: due.AddDays(-1)));
        Assert.False(DepositPolicy.IsOverdue(due, today: due));
        Assert.True(DepositPolicy.IsOverdue(due, today: due.AddDays(1)));
        Assert.False(DepositPolicy.IsOverdue(null, Today));
    }

    [Fact]
    public void IsReminderDue_WhenWaitingPastTheDueDateAndNotYetSent()
    {
        Assert.True(DepositPolicy.IsReminderDue(
            BookingStatus.AwaitingDeposit, dueDate: Today.AddDays(-1), reminderSentAt: null, Today));
    }

    [Fact]
    public void IsReminderDue_NotOnTheDueDateItself()
    {
        Assert.False(DepositPolicy.IsReminderDue(
            BookingStatus.AwaitingDeposit, dueDate: Today, reminderSentAt: null, Today));
    }

    [Fact]
    public void IsReminderDue_OnlyOnce()
    {
        Assert.False(DepositPolicy.IsReminderDue(
            BookingStatus.AwaitingDeposit, dueDate: Today.AddDays(-3),
            reminderSentAt: new DateTime(2026, 10, 7, 1, 0, 0, DateTimeKind.Utc), Today));
    }

    /// <summary>A due date stays on the booking after it leaves the waiting status (it records that a
    /// deposit was asked for) — so the status, not the date, must decide.</summary>
    [Theory]
    [InlineData(BookingStatus.Approved)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.DetailsComplete)]
    public void IsReminderDue_NeverOnceTheBookingHasLeftTheWaitingStatus(BookingStatus status)
    {
        Assert.False(DepositPolicy.IsReminderDue(status, dueDate: Today.AddDays(-3), reminderSentAt: null, Today));
    }

    // ── The price and the deposit (FR-38/115, §5.6) ─────────────────────────────

    [Theory]
    [InlineData(21_250, 4_250)]
    [InlineData(10_000, 2_000)]
    [InlineData(12_345, 2_469)] // 2 469.0 exactly
    [InlineData(12_347, 2_469)] // 2 469.4 → down
    [InlineData(12_348, 2_470)] // 2 469.6 → up
    [InlineData(12_342, 2_468)] // 2 468.4 → down
    [InlineData(1, 0)]          // 0.2 → down
    [InlineData(3, 1)]          // 0.6 → up
    public void DepositFor_IsTwentyPercent_RoundedToWholeKronor(int price, int expected)
    {
        Assert.Equal((decimal)expected, DepositPolicy.DepositFor(price));
    }

    [Fact]
    public void DepositFor_RoundsAnExactHalfUp()
    {
        // 12 342.5 is not enterable (whole kronor only), but the rounding rule is pinned anyway:
        // 2 468.5 → 2 469, half away from zero, not banker's rounding to the even 2 468.
        Assert.Equal(2_469m, DepositPolicy.DepositFor(12_342.5m));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(21_250)]
    [InlineData(5_000_000)] // the bound itself is allowed
    public void ValidatePrice_AcceptsWholeKronorWithinBounds(int price)
    {
        Assert.Null(DepositPolicy.ValidatePrice(price));
    }

    [Fact]
    public void ValidatePrice_RequiresAPrice()
    {
        Assert.Equal("Ange bokningens beräknade pris.", DepositPolicy.ValidatePrice(null));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    public void ValidatePrice_RefusesZeroOrLess(string price)
    {
        Assert.Equal("Det beräknade priset måste vara större än noll.", DepositPolicy.ValidatePrice(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ValidatePrice_RefusesAnAbsurdAmount()
    {
        Assert.Equal("Det beräknade priset verkar orimligt högt. Kontrollera beloppet.", DepositPolicy.ValidatePrice(5_000_001m));
    }

    [Fact]
    public void ValidatePrice_RefusesOre()
    {
        Assert.Equal("Ange det beräknade priset i hela kronor.", DepositPolicy.ValidatePrice(21_250.50m));
    }

    [Theory]
    [InlineData(21_250, "21\u00A0250\u00A0kr")]
    [InlineData(950, "950\u00A0kr")]
    [InlineData(1_250_000, "1\u00A0250\u00A0000\u00A0kr")]
    public void Kronor_GroupsDigitsTheSwedishWay_WithNonBreakingSpaces(int amount, string expected)
    {
        // Non-breaking, so "21 250 kr" never wraps across lines in an email or the admin rail.
        Assert.Equal(expected, BookingDisplay.Kronor(amount));
    }
}
