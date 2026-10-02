using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// How a job run's outcome reaches the admin (FR-114): the failure reason, and the label + badge the
/// job history shows. Whether a real run is recorded as failed is <c>JobHistoryIntegrationTests</c>.
/// </summary>
public class JobRunOutcomeTests
{
    [Fact]
    public void A_thrown_exception_gives_a_fixed_swedish_reason_with_nothing_from_the_exception()
    {
        // The message can carry guest data (NFR-37); the type name is English (NFR-32).
        var reason = JobRunRecorder.FailureReason(
            new InvalidOperationException("Kunde inte nå anna@example.se"), failedSends: 0);

        Assert.Equal("Jobbet avbröts av ett tekniskt fel. Se loggarna för detaljer.", reason);
        Assert.DoesNotContain("@", reason);
        Assert.DoesNotContain("Exception", reason);
    }

    [Theory]
    [InlineData(1, "1 mejl kunde inte skickas.")]
    [InlineData(3, "3 mejl kunde inte skickas.")]
    public void Failed_sends_are_counted(int failedSends, string expected)
    {
        Assert.Equal(expected, JobRunRecorder.FailureReason(failure: null, failedSends));
    }

    [Fact]
    public void A_clean_run_has_no_reason()
    {
        Assert.Null(JobRunRecorder.FailureReason(failure: null, failedSends: 0));
    }

    [Theory]
    [InlineData(JobRunOutcome.Succeeded, "Lyckades", "badge badge--approved")]
    [InlineData(JobRunOutcome.Failed, "Misslyckades", "badge badge--denied")]
    [InlineData(null, "Ej avslutad", "badge badge--neutral")]
    public void Each_outcome_has_its_label_and_the_matching_status_badge(
        JobRunOutcome? outcome, string label, string badge)
    {
        // Green and red are the existing booking status badges — the colour never travels alone.
        Assert.Equal(label, BookingDisplay.JobRunOutcomeLabel(outcome));
        Assert.Equal(badge, BookingDisplay.JobRunOutcomeBadgeClass(outcome));
    }
}
