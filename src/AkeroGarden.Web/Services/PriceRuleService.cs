using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Services;

/// <summary>
/// CRUD and lifecycle rules for <see cref="PriceRule"/> reference data (FR-31–34).
/// Unlike the other reference entities, a price rule is never referenced by a booking
/// (it is displayed read-only — FR-34, erd.md), so there is no hard-delete guard:
/// <see cref="DeleteAsync"/> always removes. Deactivation (FR-33) is still offered for
/// rules an admin wants to retire while keeping history.
/// </summary>
public class PriceRuleService(ApplicationDbContext db)
{
    /// <summary>All price rules, active and inactive, ordered by label.</summary>
    public async Task<List<PriceRule>> GetAllAsync() =>
        await db.PriceRules
            .OrderBy(p => p.Label)
            .ToListAsync();

    public async Task<PriceRule?> GetAsync(Guid id) =>
        await db.PriceRules.FindAsync(id);

    public async Task CreateAsync(PriceRule priceRule)
    {
        priceRule.Id = Guid.NewGuid();
        db.PriceRules.Add(priceRule);
        await db.SaveChangesAsync();
    }

    /// <summary>Updates the editable fields of an existing price rule (FR-32).</summary>
    public async Task UpdateAsync(PriceRule input)
    {
        var existing = await db.PriceRules.FindAsync(input.Id);
        if (existing is null) return;

        existing.Label = input.Label;
        existing.AmountExVat = input.AmountExVat;
        existing.VatRate = input.VatRate;
        existing.Unit = input.Unit;
        existing.SeasonStart = input.SeasonStart;
        existing.SeasonEnd = input.SeasonEnd;
        existing.Notes = input.Notes;
        await db.SaveChangesAsync();
    }

    /// <summary>Deactivates (soft) or reactivates a price rule without deleting it (FR-33).</summary>
    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var priceRule = await db.PriceRules.FindAsync(id);
        if (priceRule is null) return;

        priceRule.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Hard-deletes a price rule. Always permitted — price rules are standalone reference
    /// data with no referencing entity (FR-34, erd.md), so no guard applies.
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var priceRule = await db.PriceRules.FindAsync(id);
        if (priceRule is null) return;

        db.PriceRules.Remove(priceRule);
        await db.SaveChangesAsync();
    }
}
