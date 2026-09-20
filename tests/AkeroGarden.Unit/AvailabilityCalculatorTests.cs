using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure availability-state computation (NFR-26, FR-09/FR-10): a date is free
/// only when nothing at all holds it. Which reasons apply to a given date is
/// <c>OccupancyProviderTests</c>; whether a whole requested stay fits is
/// <c>AvailabilityServiceTests</c>.
/// </summary>
public class AvailabilityCalculatorTests
{
    [Fact]
    public void NoReason_IsFree()
    {
        Assert.Equal(AvailabilityState.Free, AvailabilityCalculator.Evaluate(UnavailableReason.None));
    }

    [Theory]
    [InlineData(UnavailableReason.Occupied)]
    [InlineData(UnavailableReason.Turnover)]
    [InlineData(UnavailableReason.WholePropertyBlock)]
    public void AnySingleReason_IsUnavailable(UnavailableReason reason)
    {
        Assert.Equal(AvailabilityState.Unavailable, AvailabilityCalculator.Evaluate(reason));
    }

    [Fact]
    public void SeveralReasonsTogether_AreStillJustUnavailable()
    {
        var reasons = UnavailableReason.Occupied
            | UnavailableReason.Turnover
            | UnavailableReason.WholePropertyBlock;

        Assert.Equal(AvailabilityState.Unavailable, AvailabilityCalculator.Evaluate(reasons));
    }
}
