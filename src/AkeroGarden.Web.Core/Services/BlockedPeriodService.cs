using System.ComponentModel.DataAnnotations;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// CRUD for availability blocks (requirements.md §3.3, §8.10, FR-40, FR-21). Blocks are
/// transient operational data — unlike reference data they are hard-deleted, not deactivated.
/// Validation runs fully server-side: the entity's own scope/date rules
/// (<see cref="BlockedPeriod.Validate"/>) plus existence of the referenced building/room (which
/// are logical references, not enforced FKs).
/// </summary>
public class BlockedPeriodService(ApplicationDbContext db)
{
    /// <summary>All blocks, soonest start first — for the admin list and calendar (FR-37/40).</summary>
    public async Task<List<BlockedPeriod>> GetAllAsync() =>
        await db.BlockedPeriods.OrderBy(p => p.StartDate).ThenBy(p => p.EndDate).ToListAsync();

    public async Task<BlockedPeriod?> GetAsync(Guid id) =>
        await db.BlockedPeriods.FindAsync(id);

    public async Task<SaveResult> TryCreateAsync(BlockedPeriod block)
    {
        var error = await ValidateAsync(block);
        if (error is not null) return SaveResult.Fail(error);

        block.Id = Guid.NewGuid();
        block.CreatedAt = DateTime.UtcNow;
        db.BlockedPeriods.Add(block);
        await db.SaveChangesAsync();
        return SaveResult.Success;
    }

    public async Task<SaveResult> TryUpdateAsync(BlockedPeriod input)
    {
        var existing = await db.BlockedPeriods.FindAsync(input.Id);
        if (existing is null) return SaveResult.Fail("Blockeringen kunde inte hittas.");

        var error = await ValidateAsync(input);
        if (error is not null) return SaveResult.Fail(error);

        existing.Scope = input.Scope;
        existing.BuildingId = input.BuildingId;
        existing.RoomId = input.RoomId;
        existing.StartDate = input.StartDate;
        existing.EndDate = input.EndDate;
        existing.Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
        await db.SaveChangesAsync();
        return SaveResult.Success;
    }

    /// <summary>Hard-deletes a block (FR-40) — blocks carry no history to preserve.</summary>
    public async Task DeleteAsync(Guid id)
    {
        var block = await db.BlockedPeriods.FindAsync(id);
        if (block is null) return;
        db.BlockedPeriods.Remove(block);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Full server-side validation: the entity's scope/date rules, then existence of the
    /// referenced building/room (and that the room belongs to the given building). Returns a
    /// Swedish error message, or null when valid.
    /// </summary>
    private async Task<string?> ValidateAsync(BlockedPeriod block)
    {
        // Scope/date consistency (BlockedPeriod.Validate + any data annotations).
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(block, new ValidationContext(block), results, validateAllProperties: true))
            return results.First().ErrorMessage;

        if (block.Scope is BlockScope.Building or BlockScope.Room)
        {
            var building = await db.Buildings.FindAsync(block.BuildingId!.Value);
            if (building is null) return "Den valda byggnaden finns inte.";
        }

        if (block.Scope == BlockScope.Room)
        {
            var room = await db.Rooms.FindAsync(block.RoomId!.Value);
            if (room is null) return "Det valda rummet finns inte.";
            if (room.BuildingId != block.BuildingId) return "Rummet tillhör inte den valda byggnaden.";
        }

        return null;
    }
}
