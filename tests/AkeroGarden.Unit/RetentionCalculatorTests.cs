using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the GDPR retention-date computation of §2.7 / FR-91 (NFR-26).
/// </summary>
public class RetentionCalculatorTests
{
    private static readonly DateOnly Submission = new(2026, 1, 15);
    private static readonly DateOnly CheckOut = new(2026, 8, 20);
    private static readonly DateOnly TransitionDate = new(2026, 3, 10);

    [Fact]
    public void Pending_Is12MonthsFromSubmission()
    {
        var result = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Pending, Submission, checkOut: null, TransitionDate);
        Assert.Equal(new DateOnly(2027, 1, 15), result);
    }

    [Fact]
    public void Closed_Is12MonthsFromSubmission()
    {
        var result = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Closed, Submission, checkOut: null, TransitionDate);
        Assert.Equal(new DateOnly(2027, 1, 15), result);
    }

    [Fact]
    public void Denied_Is12MonthsFromTransitionDate()
    {
        var result = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Denied, Submission, checkOut: null, TransitionDate);
        Assert.Equal(new DateOnly(2027, 3, 10), result);
    }

    [Fact]
    public void Cancelled_Is12MonthsFromTransitionDate()
    {
        var result = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Cancelled, Submission, CheckOut, TransitionDate);
        Assert.Equal(new DateOnly(2027, 3, 10), result);
    }

    [Fact]
    public void Completed_Is2YearsFromCheckOut()
    {
        var result = RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Completed, Submission, CheckOut, TransitionDate);
        Assert.Equal(new DateOnly(2028, 8, 20), result);
    }

    [Fact]
    public void Completed_WithoutCheckOut_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RetentionCalculator.ComputeDeleteAfter(
            BookingStatus.Completed, Submission, checkOut: null, TransitionDate));
    }

    [Theory]
    [InlineData(BookingStatus.Approved)]
    [InlineData(BookingStatus.DetailsComplete)]
    [InlineData(BookingStatus.Active)]
    public void LiveInProgressStates_AreNotDeletable(BookingStatus status)
    {
        var result = RetentionCalculator.ComputeDeleteAfter(status, Submission, CheckOut, TransitionDate);
        Assert.Null(result);
    }
}
