using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>Unit coverage for the booking-reference format (§8.1, FR-92) — NFR-26.</summary>
public class BookingReferencePolicyTests
{
    [Fact]
    public void Format_ProducesPrefixYearAndZeroPaddedSequence()
    {
        Assert.Equal("AG-2026-0042", BookingReferencePolicy.Format(42, 2026));
    }

    [Fact]
    public void Format_PadsSmallSequenceNumbers()
    {
        Assert.Equal("AG-2026-0001", BookingReferencePolicy.Format(1, 2026));
    }

    [Fact]
    public void Format_DoesNotTruncateSequenceNumbersBeyondFourDigits()
    {
        // The global sequence never resets per year (by design — see requirements.md v0.42), so a
        // busy property will eventually exceed 9999; the reference must keep growing, not wrap.
        Assert.Equal("AG-2026-12345", BookingReferencePolicy.Format(12345, 2026));
    }
}
