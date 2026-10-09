using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure 14-day email decisions (§9, FR-63/64): which bookings are due, the
/// window edges, the nudge's 3-day grace period, and — the point of the split — that the pre-arrival
/// reminder and the completion nudge answer to separate flags.
/// </summary>
public class ReminderScheduleTests
{
    private static readonly DateOnly Today = new(2026, 7, 5);
    private static readonly DateTime Now = new(2026, 7, 5, 3, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(BookingStatus.Approved, true)]
    [InlineData(BookingStatus.DetailsComplete, true)]
    [InlineData(BookingStatus.Active, true)]
    [InlineData(BookingStatus.Pending, false)]
    [InlineData(BookingStatus.AwaitingDeposit, false)] // no link to chase, no details to remind about (FR-63)
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.Denied, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.Closed, false)]
    public void IsReminderEligible_OnlyForApprovedThroughActive(BookingStatus status, bool expected) =>
        Assert.Equal(expected, ReminderSchedule.IsReminderEligible(status));

    [Fact]
    public void IsInLeadWindow_CoversTodayThrough14Days()
    {
        Assert.True(ReminderSchedule.IsInLeadWindow(Today, Today));                 // check-in today
        Assert.True(ReminderSchedule.IsInLeadWindow(Today.AddDays(14), Today));     // far edge
        Assert.False(ReminderSchedule.IsInLeadWindow(Today.AddDays(15), Today));    // too far
        Assert.False(ReminderSchedule.IsInLeadWindow(Today.AddDays(-1), Today));    // already past
        Assert.False(ReminderSchedule.IsInLeadWindow(null, Today));
    }

    // ── Pre-arrival reminder (FR-64) ──────────────────────────────────────────────

    [Fact]
    public void PreArrivalReminder_DueOnceDetailsAreIn()
    {
        Assert.True(ReminderSchedule.IsPreArrivalReminderDue(
            BookingStatus.DetailsComplete, Today.AddDays(7), null, Today));
        Assert.True(ReminderSchedule.IsPreArrivalReminderDue(
            BookingStatus.Active, Today.AddDays(7), null, Today));
    }

    [Fact]
    public void PreArrivalReminder_NotDueWhileStillAwaitingCompletion()
    {
        // An Approved booking gets the nudge instead — it has no participant counts or times to
        // remind anyone of yet.
        Assert.False(ReminderSchedule.IsPreArrivalReminderDue(
            BookingStatus.Approved, Today.AddDays(7), null, Today));
    }

    [Fact]
    public void PreArrivalReminder_NotDueTwice()
    {
        Assert.False(ReminderSchedule.IsPreArrivalReminderDue(
            BookingStatus.DetailsComplete, Today.AddDays(7), Now.AddDays(-1), Today));
    }

    // ── Completion nudge (FR-63) ──────────────────────────────────────────────────

    [Fact]
    public void CompletionNudge_DueWhenLinkIsOlderThanTheGrace()
    {
        Assert.True(ReminderSchedule.IsCompletionNudgeDue(
            BookingStatus.Approved, Today.AddDays(7), null, Now.AddDays(-4), Today, Now));
    }

    [Fact]
    public void CompletionNudge_HeldWhileTheLinkIsFresh()
    {
        // Approved this afternoon must not be told at 03:00 tomorrow that they have not filled it in.
        Assert.False(ReminderSchedule.IsCompletionNudgeDue(
            BookingStatus.Approved, Today.AddDays(7), null, Now.AddHours(-13), Today, Now));
        Assert.False(ReminderSchedule.IsCompletionNudgeDue(
            BookingStatus.Approved, Today.AddDays(7), null, Now.AddDays(-3), Today, Now)); // exactly 3d
    }

    [Fact]
    public void CompletionNudge_IsDeferredNotSkipped()
    {
        // Same booking, same unchanged link, a later run: the grace has passed, so the nudge that was
        // withheld now fires. A skipped nudge would be false on both runs.
        var linkSentAt = Now.AddHours(-13);
        Assert.False(ReminderSchedule.IsCompletionNudgeDue(
            BookingStatus.Approved, Today.AddDays(7), null, linkSentAt, Today, Now));
        Assert.True(ReminderSchedule.IsCompletionNudgeDue(
            BookingStatus.Approved, Today.AddDays(4), null, linkSentAt, Today.AddDays(3), Now.AddDays(3)));
    }

    [Fact]
    public void CompletionNudge_DueImmediatelyWhenNoLinkWasEverSent()
    {
        // Nothing was sent, so there is nothing the guest could have ignored and no grace to serve —
        // and the nudge is what reissues the link. Withholding it would strand exactly the booking
        // most in need of one.
        Assert.True(ReminderSchedule.IsCompletionNudgeDue(
            BookingStatus.Approved, Today.AddDays(7), null, null, Today, Now));
    }

    [Fact]
    public void CompletionNudge_NotDueTwice_NorOnceDetailsAreIn()
    {
        Assert.False(ReminderSchedule.IsCompletionNudgeDue(
            BookingStatus.Approved, Today.AddDays(7), Now.AddDays(-1), Now.AddDays(-4), Today, Now));
        Assert.False(ReminderSchedule.IsCompletionNudgeDue(
            BookingStatus.DetailsComplete, Today.AddDays(7), null, Now.AddDays(-4), Today, Now));
    }

    // ── The split itself (FR-63, §8.1) ────────────────────────────────────────────

    [Fact]
    public void NudgedGuestWhoThenSubmits_IsStillDueThePreArrivalReminder()
    {
        // The defect the split exists to fix. Nudged at 14 days (CompletionNudgeSentAt set), submits
        // at 12, and at 10 days out the pre-arrival email with the address and times must still be
        // due. While one flag guarded both, this was false and the guest got nothing.
        var nudgedAt = Now;
        var laterToday = Today.AddDays(4);

        Assert.True(ReminderSchedule.IsPreArrivalReminderDue(
            BookingStatus.DetailsComplete, Today.AddDays(14), reminderSentAt: null, laterToday));

        // …and the nudge does not fire a second time off the back of it.
        Assert.False(ReminderSchedule.IsCompletionNudgeDue(
            BookingStatus.DetailsComplete, Today.AddDays(14), nudgedAt, Now.AddDays(-5), laterToday, Now.AddDays(4)));
    }
}
