using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the cleaning-buffer window a booking holds after its stay (NFR-26, FR-106):
/// the check-out date and the day after it. The rule lives in one place so the public calendar and
/// the admin calendar cannot disagree — these tests pin the rule itself, independently of either.
/// </summary>
public class TurnoverWindowTests
{
    private static readonly DateOnly CheckOut = new(2026, 6, 10);

    [Fact]
    public void HoldsTwoDays()
    {
        Assert.Equal(2, TurnoverWindow.Days);
    }

    [Fact]
    public void Covers_CheckOutDateItself()
    {
        // The departing group's beds are free that night, but the day is not bookable — this is
        // the change FR-106 makes to the previous nightly-only semantics.
        Assert.True(TurnoverWindow.Covers(CheckOut, CheckOut));
    }

    [Fact]
    public void Covers_DayAfterCheckOut()
    {
        Assert.True(TurnoverWindow.Covers(CheckOut, CheckOut.AddDays(1)));
    }

    [Fact]
    public void DoesNotCover_TheDayBeforeCheckOut()
    {
        // The last night of the stay is occupancy, not turnover. It is unavailable for a different
        // reason, and conflating the two would extend the buffer to three days.
        Assert.False(TurnoverWindow.Covers(CheckOut, CheckOut.AddDays(-1)));
    }

    [Fact]
    public void DoesNotCover_TwoDaysAfterCheckOut()
    {
        // The next group may arrive here — this boundary is the whole cost of the rule.
        Assert.False(TurnoverWindow.Covers(CheckOut, CheckOut.AddDays(2)));
    }

    [Fact]
    public void LastDay_IsTheDayAfterCheckOut()
    {
        Assert.Equal(CheckOut.AddDays(1), TurnoverWindow.LastDay(CheckOut));
    }

    [Fact]
    public void Dates_AreTheCheckOutDateAndTheDayAfter_InOrder()
    {
        Assert.Equal(new[] { CheckOut, CheckOut.AddDays(1) }, TurnoverWindow.Dates(CheckOut));
    }

    [Fact]
    public void EarliestCheckOutAffecting_ReachesBackFarEnoughToCatchAStayThatAlreadyEnded()
    {
        // A stay that checked out the day before a calendar window opens still holds the window's
        // first day. A query filtering on `CheckOut >= from` would drop that booking and silently
        // show the day as free.
        var from = new DateOnly(2026, 6, 1);
        var earliest = TurnoverWindow.EarliestCheckOutAffecting(from);

        Assert.Equal(from.AddDays(-1), earliest);
        Assert.True(TurnoverWindow.Covers(earliest, from));
    }

    [Fact]
    public void EarliestCheckOutAffecting_ExcludesStaysWhoseWindowClosedBeforeTheRange()
    {
        var from = new DateOnly(2026, 6, 1);
        var tooEarly = TurnoverWindow.EarliestCheckOutAffecting(from).AddDays(-1);

        Assert.False(TurnoverWindow.Covers(tooEarly, from));
    }
}
