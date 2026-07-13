namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// Who performed an audited action (§6.9, §8.15). Every change to a booking is attributable to
/// exactly one of these: an <see cref="Admin"/> (an authenticated admin acting in the panel), a
/// <see cref="Guest"/> (the booking's contact person via the completion magic link — no account,
/// FR-54), or the <see cref="System"/> (an automated background job, e.g. the 14-day reminder or
/// the retention deletion, FR-63/69).
/// </summary>
public enum AuditActorType
{
    Admin,
    Guest,
    System,
}
