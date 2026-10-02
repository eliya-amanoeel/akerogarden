using System.ComponentModel.DataAnnotations;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// CRUD for availability blocks (requirements.md §3.3, §8.10, FR-40, FR-21). Blocks are
/// transient operational data — unlike reference data they are hard-deleted, not deactivated.
/// Validation runs fully server-side: the entity's own scope/date rules
/// (<see cref="BlockedPeriod.Validate"/>), existence of the referenced building/room (which
/// are logical references, not enforced FKs), and that the block does not land on a building a
/// live booking already has assigned (FR-102, see <see cref="GetAssignmentConflictsAsync"/>).
/// </summary>
public class BlockedPeriodService(ApplicationDbContext db)
{
    /// <summary>
    /// Bookings whose assigned buildings a block may not close (FR-102): every booking that is not
    /// finished. <c>Pending</c> is included on purpose — an assignment is made before approval
    /// (FR-103), and a block landing on it afterwards is exactly how a group came to be approved
    /// into a closed building.
    /// </summary>
    private static readonly BookingStatus[] LiveStatuses =
        [BookingStatus.Pending, .. OccupancyProvider.OccupyingStatuses];

    /// <summary>All blocks, soonest start first — for the admin list and calendar (FR-37/40).</summary>
    public async Task<List<BlockedPeriod>> GetAllAsync() =>
        await db.BlockedPeriods.OrderBy(p => p.StartDate).ThenBy(p => p.EndDate).ToListAsync();

    public async Task<BlockedPeriod?> GetAsync(Guid id) =>
        await db.BlockedPeriods.FindAsync(id);

    /// <summary>
    /// The live bookings (<see cref="LiveStatuses"/>) that have a building assigned which
    /// <paramref name="block"/> would close over their stay, earliest first (FR-102). A
    /// <see cref="BlockScope.Building"/> block conflicts with bookings that have that building; a
    /// <see cref="BlockScope.WholeProperty"/> block with bookings that have any building. A
    /// <see cref="BlockScope.Room"/> block never does — it only removes beds, which the approval
    /// capacity warning already reports (FR-103).
    ///
    /// <para>The reverse of FR-102's original rule: a blocked building cannot be assigned, and an
    /// assigned building cannot be blocked. With only the first half, a block created after the
    /// assignment left the building ticked and greyed out, and approval went through with the guest's
    /// email naming a building closed for their whole stay. Stay dates use the same overlap as
    /// assignment (check-out day exclusive), so the two halves agree on what "overlaps" means.</para>
    /// </summary>
    public async Task<List<BlockAssignmentConflict>> GetAssignmentConflictsAsync(BlockedPeriod block)
    {
        if (block.Scope is not (BlockScope.Building or BlockScope.WholeProperty))
            return [];

        var wholeProperty = block.Scope == BlockScope.WholeProperty;
        var buildingId = block.BuildingId;

        return await db.Bookings
            .Where(b => LiveStatuses.Contains(b.Status)
                && b.CheckIn != null && b.CheckOut != null
                && block.StartDate < b.CheckOut && block.EndDate >= b.CheckIn
                && b.BuildingAssignments.Any(a => wholeProperty || a.BuildingId == buildingId))
            .OrderBy(b => b.CheckIn)
            .Select(b => new BlockAssignmentConflict(b.Id, b.BookingReference))
            .ToListAsync();
    }

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

        Building? building = null;
        if (block.Scope is BlockScope.Building or BlockScope.Room)
        {
            building = await db.Buildings.FindAsync(block.BuildingId!.Value);
            if (building is null) return "Den valda byggnaden finns inte.";
        }

        if (block.Scope == BlockScope.Room)
        {
            var room = await db.Rooms.FindAsync(block.RoomId!.Value);
            if (room is null) return "Det valda rummet finns inte.";
            if (room.BuildingId != block.BuildingId) return "Rummet tillhör inte den valda byggnaden.";
        }

        var conflicts = await GetAssignmentConflictsAsync(block);
        if (conflicts.Count > 0)
            return DescribeConflicts(block, building?.Name, conflicts);

        return null;
    }

    /// <summary>The Swedish refusal for a block that would close an assigned building (FR-102).</summary>
    public static string DescribeConflicts(
        BlockedPeriod block, string? buildingName, IReadOnlyList<BlockAssignmentConflict> conflicts)
    {
        var references = string.Join(", ", conflicts.Select(c => c.BookingReference));
        var bookings = conflicts.Count == 1 ? "bokningen" : "bokningarna";
        return block.Scope == BlockScope.WholeProperty
            ? $"Hela anläggningen kan inte blockeras: {references} har byggnader tilldelade under perioden. "
                + $"Avmarkera byggnaderna på {bookings} eller välj andra datum."
            : $"Byggnaden {buildingName} är tilldelad {references} under perioden. "
                + $"Avmarkera byggnaden på {bookings} innan du blockerar den.";
    }
}

/// <summary>A live booking whose assigned building a block would close (FR-102).</summary>
public record BlockAssignmentConflict(Guid BookingId, string BookingReference);
