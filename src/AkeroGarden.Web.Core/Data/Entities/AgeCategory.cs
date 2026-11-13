namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// A participant's age group (requirements.md §8.5, §5.1/5.2). Fixed system values — not
/// admin-managed. Two groups, because the client prices by one line only: <see cref="Child"/> =
/// 0–3 years (free), <see cref="Adult"/> = 4 and over (standard per-person rate). No price is
/// computed from it (C8) — the admin reads the group off the participant list when working out the
/// price they enter (§5.6). Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum AgeCategory
{
    Adult,
    Child
}
