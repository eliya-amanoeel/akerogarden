using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the completion-token expiry and generation rules of §2.6 / FR-53 and
/// NFR-03 (NFR-26).
/// </summary>
public class CompletionTokenPolicyTests
{
    private static readonly DateTime Issued = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FarFutureCheckIn_ExpiresAt30DayCeiling()
    {
        // Check-in far off → the 30-day ceiling is the earliest, well before "14 days before check-in".
        var checkIn = new DateOnly(2026, 12, 1);
        var expiry = CompletionTokenPolicy.ComputeExpiry(Issued, checkIn);
        Assert.Equal(Issued.AddDays(30), expiry);
    }

    [Fact]
    public void NearCheckIn_ExpiresAt14DaysBeforeCheckIn()
    {
        // Check-in 20 days out → 14 days before check-in (at 14:00) is earlier than the 30-day
        // ceiling and later than the 48h floor.
        var checkIn = new DateOnly(2026, 7, 21);
        var expiry = CompletionTokenPolicy.ComputeExpiry(Issued, checkIn);
        Assert.Equal(new DateTime(2026, 7, 7, 14, 0, 0, DateTimeKind.Utc), expiry);
    }

    [Fact]
    public void VeryCloseCheckIn_IsFlooredTo48Hours()
    {
        // Check-in only 4 days out → "14 days before check-in" is already in the past;
        // the 48-hour floor guarantees the token is still usable (FR-53).
        var checkIn = new DateOnly(2026, 7, 5);
        var expiry = CompletionTokenPolicy.ComputeExpiry(Issued, checkIn);
        Assert.Equal(Issued.AddHours(48), expiry);
    }

    [Fact]
    public void CheckInInThePast_IsFlooredTo48Hours()
    {
        var checkIn = new DateOnly(2026, 6, 1);
        var expiry = CompletionTokenPolicy.ComputeExpiry(Issued, checkIn);
        Assert.Equal(Issued.AddHours(48), expiry);
    }

    [Fact]
    public void GenerateToken_IsUrlSafe_And256Bits()
    {
        var token = CompletionTokenPolicy.GenerateToken();

        Assert.False(string.IsNullOrEmpty(token));
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);

        // Decodes back to 32 bytes (256 bits — comfortably above the 128-bit minimum, NFR-03).
        var padded = token.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        Assert.Equal(32, Convert.FromBase64String(padded).Length);
    }

    [Fact]
    public void GenerateToken_ProducesDistinctValues()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => CompletionTokenPolicy.GenerateToken()).ToHashSet();
        Assert.Equal(100, tokens.Count);
    }

    [Fact]
    public void IsValidAt_TrueBeforeExpiry_FalseAtOrAfter()
    {
        var expiry = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(CompletionTokenPolicy.IsValidAt(expiry, expiry.AddSeconds(-1)));
        Assert.False(CompletionTokenPolicy.IsValidAt(expiry, expiry));            // rejected at the instant
        Assert.False(CompletionTokenPolicy.IsValidAt(expiry, expiry.AddSeconds(1)));
    }
}
