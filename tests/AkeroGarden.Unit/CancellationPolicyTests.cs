using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the deposit-outcome resolution of §5.6 / FR-79 (NFR-26), including the
/// exact "two months before check-in" boundary.
/// </summary>
public class CancellationPolicyTests
{
    private static readonly DateOnly CheckIn = new(2026, 6, 1);

    [Theory]
    [InlineData("2026-05-31")] // one day before check-in
    [InlineData("2026-04-01")] // exactly two months before
    [InlineData("2026-01-01")] // long before
    public void PropertyInitiated_IsAlwaysFullRefund(string cancelledOn)
    {
        var result = CancellationPolicy.ResolveDepositOutcome(
            CancellationInitiator.PropertyInitiated, CheckIn, DateOnly.Parse(cancelledOn));
        Assert.Equal(DepositOutcome.FullRefund, result);
    }

    [Fact]
    public void GuestInitiated_MoreThanTwoMonthsBefore_IsFullRefund()
    {
        var result = CancellationPolicy.ResolveDepositOutcome(
            CancellationInitiator.GuestInitiated, CheckIn, new DateOnly(2026, 3, 31));
        Assert.Equal(DepositOutcome.FullRefund, result);
    }

    [Fact]
    public void GuestInitiated_ExactlyTwoMonthsBefore_IsForfeited()
    {
        // "Two months or less before arrival" forfeits — the boundary day is inclusive.
        var result = CancellationPolicy.ResolveDepositOutcome(
            CancellationInitiator.GuestInitiated, CheckIn, new DateOnly(2026, 4, 1));
        Assert.Equal(DepositOutcome.Forfeited, result);
    }

    [Fact]
    public void GuestInitiated_LessThanTwoMonthsBefore_IsForfeited()
    {
        var result = CancellationPolicy.ResolveDepositOutcome(
            CancellationInitiator.GuestInitiated, CheckIn, new DateOnly(2026, 5, 15));
        Assert.Equal(DepositOutcome.Forfeited, result);
    }
}
