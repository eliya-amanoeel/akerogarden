using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the one-way booking status transitions of §2.5 (NFR-26). The state
/// machine is the single source of truth; these tests pin every allowed transition and a
/// representative set of the illegal ones — including all transitions out of terminal states.
/// </summary>
public class BookingStateMachineTests
{
    public static readonly TheoryData<BookingStatus, BookingStatus> AllowedTransitions = new()
    {
        { BookingStatus.Pending, BookingStatus.Approved },
        { BookingStatus.Pending, BookingStatus.Denied },
        { BookingStatus.Pending, BookingStatus.Closed },
        { BookingStatus.Approved, BookingStatus.DetailsComplete },
        { BookingStatus.Approved, BookingStatus.Cancelled },
        { BookingStatus.DetailsComplete, BookingStatus.Active },
        { BookingStatus.DetailsComplete, BookingStatus.Cancelled },
        { BookingStatus.Active, BookingStatus.Completed },
        { BookingStatus.Active, BookingStatus.Cancelled },
    };

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void CanTransition_True_ForAllowed(BookingStatus from, BookingStatus to)
    {
        Assert.True(BookingStateMachine.CanTransition(from, to));
    }

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void EnsureCanTransition_DoesNotThrow_ForAllowed(BookingStatus from, BookingStatus to)
    {
        var ex = Record.Exception(() => BookingStateMachine.EnsureCanTransition(from, to));
        Assert.Null(ex);
    }

    [Theory]
    // Terminal states have no outbound transitions (§2.5).
    [InlineData(BookingStatus.Denied, BookingStatus.Approved)]
    [InlineData(BookingStatus.Completed, BookingStatus.Active)]
    [InlineData(BookingStatus.Closed, BookingStatus.Approved)]
    [InlineData(BookingStatus.Cancelled, BookingStatus.Approved)]
    // No skipping the completion step.
    [InlineData(BookingStatus.Approved, BookingStatus.Active)]
    [InlineData(BookingStatus.Approved, BookingStatus.Completed)]
    [InlineData(BookingStatus.Pending, BookingStatus.DetailsComplete)]
    [InlineData(BookingStatus.Pending, BookingStatus.Cancelled)] // Pending is denied, never cancelled (FR-76)
    // No going backwards.
    [InlineData(BookingStatus.DetailsComplete, BookingStatus.Approved)]
    [InlineData(BookingStatus.Active, BookingStatus.DetailsComplete)]
    // Same-state is not a transition.
    [InlineData(BookingStatus.Pending, BookingStatus.Pending)]
    [InlineData(BookingStatus.Approved, BookingStatus.Approved)]
    public void CanTransition_False_ForDisallowed(BookingStatus from, BookingStatus to)
    {
        Assert.False(BookingStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void EnsureCanTransition_Throws_ForDisallowed()
    {
        var ex = Assert.Throws<InvalidBookingTransitionException>(
            () => BookingStateMachine.EnsureCanTransition(BookingStatus.Denied, BookingStatus.Approved));
        Assert.Equal(BookingStatus.Denied, ex.From);
        Assert.Equal(BookingStatus.Approved, ex.To);
    }

    [Fact]
    public void AllowedTargets_ForPending_AreApproveDenyClose()
    {
        var targets = BookingStateMachine.AllowedTargets(BookingStatus.Pending);
        Assert.Equal(
            new[] { BookingStatus.Approved, BookingStatus.Denied, BookingStatus.Closed }.OrderBy(x => x),
            targets.OrderBy(x => x));
    }

    [Theory]
    [InlineData(BookingStatus.Denied)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Closed)]
    [InlineData(BookingStatus.Cancelled)]
    public void AllowedTargets_ForTerminalStates_AreEmpty(BookingStatus terminal)
    {
        Assert.Empty(BookingStateMachine.AllowedTargets(terminal));
    }

    [Fact]
    public void NewBooking_DefaultsToPending()
    {
        Assert.Equal(BookingStatus.Pending, new Booking().Status);
    }
}
