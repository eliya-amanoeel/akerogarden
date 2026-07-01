using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Services;

/// <summary>
/// CRUD and lifecycle rules for <see cref="MealOption"/> reference data
/// (FR-22–26, NFR-14). Business rules live here, not in the Razor component (NFR-26/27).
/// </summary>
public class MealOptionService(ApplicationDbContext db)
{
    /// <summary>All meal options, active and inactive, in admin display order.</summary>
    public async Task<List<MealOption>> GetAllAsync() =>
        await db.MealOptions
            .OrderBy(m => m.MealType)
            .ThenBy(m => m.DisplayOrder)
            .ThenBy(m => m.Name)
            .ToListAsync();

    /// <summary>Active meal options only, grouped by meal type in display order — for the public menu page (FR-83).</summary>
    public async Task<List<MealOption>> GetActiveAsync() =>
        await db.MealOptions
            .Where(m => m.IsActive)
            .OrderBy(m => m.MealType)
            .ThenBy(m => m.DisplayOrder)
            .ThenBy(m => m.Name)
            .ToListAsync();

    public async Task<MealOption?> GetAsync(Guid id) =>
        await db.MealOptions.FindAsync(id);

    public async Task CreateAsync(MealOption mealOption)
    {
        mealOption.Id = Guid.NewGuid();
        db.MealOptions.Add(mealOption);
        await db.SaveChangesAsync();
    }

    /// <summary>Updates the editable fields of an existing meal option (FR-23).</summary>
    public async Task UpdateAsync(MealOption input)
    {
        var existing = await db.MealOptions.FindAsync(input.Id);
        if (existing is null) return;

        existing.Name = input.Name;
        existing.Description = input.Description;
        existing.MealType = input.MealType;
        existing.IsVegetarian = input.IsVegetarian;
        existing.DisplayOrder = input.DisplayOrder;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deactivates (soft) or reactivates a meal option without altering existing
    /// selections on bookings (FR-24/25, NFR-14).
    /// </summary>
    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var mealOption = await db.MealOptions.FindAsync(id);
        if (mealOption is null) return;

        mealOption.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Hard-deletes a meal option only when nothing references it (FR-26); otherwise <see cref="HardDeleteResult.Blocked"/>.
    /// </summary>
    public async Task<HardDeleteResult> TryHardDeleteAsync(Guid id)
    {
        var mealOption = await db.MealOptions.FindAsync(id);
        if (mealOption is null) return HardDeleteResult.NotFound;

        if (!CanHardDelete(await CountReferencesAsync(id)))
            return HardDeleteResult.Blocked;

        db.MealOptions.Remove(mealOption);
        await db.SaveChangesAsync();
        return HardDeleteResult.Deleted;
    }

    /// <summary>
    /// Pure guard decision (unit-testable, NFR-26): a meal option is hard-deletable
    /// only when nothing references it.
    /// </summary>
    public static bool CanHardDelete(int referenceCount) => referenceCount == 0;

    /// <summary>
    /// Counts references that block a hard-delete. Phase 1: none exist yet —
    /// meal options are first referenced by MealPreference.MealOptionId in Phase 4 (FR-26), wired in then.
    /// </summary>
    private Task<int> CountReferencesAsync(Guid mealOptionId) => Task.FromResult(0);
}
