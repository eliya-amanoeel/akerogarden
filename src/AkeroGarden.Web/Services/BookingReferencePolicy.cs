namespace AkeroGarden.Web.Services;

/// <summary>
/// Pure formatting of the human-readable booking reference (§8.1, FR-92), unit-testable without a
/// database (NFR-26). The sequence number itself is DB-generated (a global monotonic identity
/// column — no per-year reset, so no locking is needed); this only renders it as
/// <c>AG-YYYY-NNNN</c>, e.g. <c>AG-2026-0042</c>.
/// </summary>
public static class BookingReferencePolicy
{
    public static string Format(int sequenceNumber, int creationYear) =>
        $"AG-{creationYear}-{sequenceNumber:D4}";
}
