using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Integration.Infrastructure;

/// <summary>
/// Gives a stay request the lodging assignment that approval now requires (FR-103/D1, §8.16).
///
/// <para>Assignment became a <b>precondition</b> of approving any booking with overnight guests when
/// per-participant room assignment was replaced (2026-08-06). Tests that approve a booking therefore
/// have to assign a building first — deliberately, rather than the precondition being softened to
/// keep the suite green: the approval email names the buildings, so approving without them would
/// send the guest an incomplete answer, and a test suite that skipped the step would stop describing
/// what the system actually does.</para>
/// </summary>
internal static class BuildingAssignmentSeed
{
    /// <summary>
    /// Assigns a lodging building to <paramref name="bookingId"/>, creating a physical
    /// building + building + room if the shared fixture database has none yet. Writes the assignment
    /// row directly rather than through <c>SetBuildingAssignmentsAsync</c>, so a test that is
    /// arranging state is not also silently exercising the FR-102 blocked-building path — the
    /// service route is asserted explicitly in <c>BuildingAssignmentIntegrationTests</c>.
    /// </summary>
    public static async Task<Guid> AssignBuildingAsync(
        ApplicationDbContext db, Guid bookingId, int bedCount = 40)
    {
        var building = await db.Buildings
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .FirstOrDefaultAsync();

        if (building is null)
        {
            var physical = new PhysicalBuilding
            {
                Id = Guid.NewGuid(),
                Name = $"Fysisk-{Guid.NewGuid():N}",
                IsActive = true,
            };
            db.PhysicalBuildings.Add(physical);

            building = new Building
            {
                Id = Guid.NewGuid(),
                Name = $"Byggnad-{Guid.NewGuid():N}",
                PhysicalBuildingId = physical.Id,
                IsActive = true,
            };
            building.Rooms.Add(new Room
            {
                Id = Guid.NewGuid(),
                BuildingId = building.Id,
                Name = "Sovsal",
                BedCount = bedCount,
                IsActive = true,
            });
            db.Buildings.Add(building);
            await db.SaveChangesAsync();
        }

        var physicalName = await db.PhysicalBuildings
            .Where(p => p.Id == building.PhysicalBuildingId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync() ?? string.Empty;

        var already = await db.BookingBuildingAssignments
            .AnyAsync(a => a.BookingId == bookingId && a.BuildingId == building.Id);
        if (!already)
        {
            db.BookingBuildingAssignments.Add(new BookingBuildingAssignment
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                BuildingId = building.Id,
                BuildingNameSnapshot = building.Name,
                PhysicalBuildingNameSnapshot = physicalName,
                AssignedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        return building.Id;
    }
}
