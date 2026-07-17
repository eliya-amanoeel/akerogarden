namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// Which of the two inquiry tracks a booking came from (requirements.md §8.1, FR-02).
/// A <see cref="GeneralInquiry"/> leaves the stay-specific fields null and can only be
/// Closed; a <see cref="StayRequest"/> runs the full approve/deny lifecycle (§2.5).
/// Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum BookingType
{
    StayRequest,
    GeneralInquiry
}
