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
}
