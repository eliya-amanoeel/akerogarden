namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// A participant's age tier (requirements.md §8.5, §5.1/5.2). Fixed system values — not
/// admin-managed. Age bands: <see cref="Infant"/> = 0–3 years (free), <see cref="Child"/> = 4–12,
/// <see cref="Adult"/> = 13+. Pricing tier: <see cref="Adult"/> and <see cref="Child"/> bill at
/// the standard per-person rate, <see cref="Infant"/> is free (the free/paid line is age 4). The
/// Child/Adult split is for kitchen portion planning only and does not affect price. No monetary
/// totals are stored (C8) — the tier is reference data the admin reads off, not a computed amount.
/// Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum AgeCategory
{
    Adult,
    Child,
    Infant
}
