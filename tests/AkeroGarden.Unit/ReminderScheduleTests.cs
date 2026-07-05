using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure 14-day reminder decisions (§9, FR-63/64): which bookings are due,
/// de-duplication by the sent marker, the window edges, and the nudge-vs-standard variant.
/// </summary>
public class ReminderScheduleTests
{
    private static readonly DateOnly Today = new(2026, 7, 5);

    [Theory]
    [InlineData(BookingStatus.Approved, true)]
    [InlineData(BookingStatus.DetailsComplete, true)]
    [InlineData(BookingStatus.Active, true)]
    [InlineData(BookingStatus.Pending, false)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.Denied, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Closed, false)]
    public void IsReminderEligible_OnlyForApprovedThroughActive(BookingStatus status, bool expected) =>
        Assert.Equal(expected, ReminderSchedule.IsReminderEligible(status));

    [Fact]
    public void IsReminderDue_WithinWindow_Eligible_NotSent_IsTrue()
    {
        Assert.True(ReminderSchedule.IsReminderDue(BookingStatus.DetailsComplete, Today.AddDays(14), null, Today)); // edge
        Assert.True(ReminderSchedule.IsReminderDue(BookingStatus.DetailsComplete, Today.AddDays(7), null, Today));
        Assert.True(ReminderSchedule.IsReminderDue(BookingStatus.Approved, Today, null, Today)); // check-in today
    }

    [Fact]
    public void IsReminderDue_OutsideWindow_IsFalse()
    {
        Assert.False(ReminderSchedule.IsReminderDue(BookingStatus.DetailsComplete, Today.AddDays(15), null, Today)); // too far
        Assert.False(ReminderSchedule.IsReminderDue(BookingStatus.DetailsComplete, Today.AddDays(-1), null, Today)); // past
    }

    [Fact]
    public void IsReminderDue_AlreadySent_OrIneligible_OrNullCheckIn_IsFalse()
    {
        Assert.False(ReminderSchedule.IsReminderDue(BookingStatus.DetailsComplete, Today.AddDays(7), DateTime.UtcNow, Today));
        Assert.False(ReminderSchedule.IsReminderDue(BookingStatus.Pending, Today.AddDays(7), null, Today));
        Assert.False(ReminderSchedule.IsReminderDue(BookingStatus.DetailsComplete, null, null, Today));
    }

    [Fact]
    public void IsNudgeVariant_OnlyForApproved()
    {
        Assert.True(ReminderSchedule.IsNudgeVariant(BookingStatus.Approved));
        Assert.False(ReminderSchedule.IsNudgeVariant(BookingStatus.DetailsComplete));
        Assert.False(ReminderSchedule.IsNudgeVariant(BookingStatus.Active));
    }
}
