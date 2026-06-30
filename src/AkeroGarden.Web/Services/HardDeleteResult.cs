namespace AkeroGarden.Web.Services;

/// <summary>
/// Outcome of an attempted hard-delete of a reference-data record.
/// Reference data is never hard-deleted while referenced (FR-16/20/26/30/88) —
/// callers deactivate (soft) instead when <see cref="Blocked"/> is returned.
/// </summary>
public enum HardDeleteResult
{
    Deleted,
    Blocked,
    NotFound
}
