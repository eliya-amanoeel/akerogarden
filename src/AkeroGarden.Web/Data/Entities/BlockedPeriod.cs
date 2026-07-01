using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// A block on availability for a date range at one scope — whole property, a building, or a
/// single room (requirements.md §3.3, §8.10). Unified entity that absorbed the former
/// <c>RoomBlock</c> (v0.25).
///
/// <para><see cref="BuildingId"/> and <see cref="RoomId"/> are <b>logical references</b>
/// validated in the service layer, not enforced foreign keys — mirroring
/// <see cref="CleaningZone.IncludesZoneIds"/>. This keeps blocking (transient operational data)
/// decoupled from the reference-data hard-delete guards, so a stale block never silently blocks
/// deleting a building or room at the DB level.</para>
/// </summary>
public class BlockedPeriod : IValidatableObject
{
    public Guid Id { get; set; }

    public BlockScope Scope { get; set; }

    /// <summary>Set for <see cref="BlockScope.Building"/> and <see cref="BlockScope.Room"/>;
    /// null for <see cref="BlockScope.WholeProperty"/>.</summary>
    public Guid? BuildingId { get; set; }

    /// <summary>Set for <see cref="BlockScope.Room"/> only; null otherwise.</summary>
    public Guid? RoomId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>Internal only — e.g. "Renovering", "Trasigt fönster".</summary>
    [StringLength(500, ErrorMessage = "Orsaken får vara högst 500 tecken.")]
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate < StartDate)
        {
            yield return new ValidationResult(
                "Slutdatumet kan inte vara före startdatumet.",
                [nameof(EndDate)]);
        }

        switch (Scope)
        {
            case BlockScope.WholeProperty when BuildingId is not null || RoomId is not null:
                yield return new ValidationResult(
                    "En blockering för hela anläggningen kan inte ha en byggnad eller ett rum.",
                    [nameof(Scope)]);
                break;
            case BlockScope.Building when BuildingId is null:
                yield return new ValidationResult(
                    "En byggnadsblockering måste ange en byggnad.",
                    [nameof(BuildingId)]);
                break;
            case BlockScope.Building when RoomId is not null:
                yield return new ValidationResult(
                    "En byggnadsblockering kan inte ange ett rum.",
                    [nameof(RoomId)]);
                break;
            case BlockScope.Room when BuildingId is null || RoomId is null:
                yield return new ValidationResult(
                    "En rumsblockering måste ange både byggnad och rum.",
                    [nameof(RoomId)]);
                break;
        }
    }
}
