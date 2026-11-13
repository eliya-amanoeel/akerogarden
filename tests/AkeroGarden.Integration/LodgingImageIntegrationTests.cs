using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// One image per lodging building and per room (client review 2026-11-11 item 13; FR-13, FR-17),
/// stored and cleaned up exactly as a facility's is (<see cref="FacilityIntegrationTests"/>): the name
/// is saved with the record, and a replaced, cleared or orphaned image is deleted from storage after
/// the save — never before it, and never on an unrelated edit. Real PostgreSQL; storage is the
/// recording <see cref="FakeImageStorage"/>.
/// </summary>
public class LodgingImageIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public LodgingImageIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider Services() => _fixture.BuildServices(s =>
    {
        s.AddScoped<BuildingService>();
        s.AddScoped<RoomService>();
    });

    private static string NewName() => $"{Guid.NewGuid():N}.webp";

    /// <summary>Seeds a house with one room, each carrying the given image, and stores both images.</summary>
    private static async Task<(Guid BuildingId, Guid RoomId)> SeedAsync(
        IServiceProvider provider, string? buildingImage, string? roomImage)
    {
        var images = provider.GetRequiredService<FakeImageStorage>();
        foreach (var name in new[] { buildingImage, roomImage }.OfType<string>())
            images.Stored[name] = [];

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pb = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Fysisk-{Guid.NewGuid():N}" };
        var building = new Building { Id = Guid.NewGuid(), Name = $"Blidö-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id, ImageName = buildingImage };
        var room = new Room { Id = Guid.NewGuid(), BuildingId = building.Id, Name = "Rum 1", BedCount = 4, ImageName = roomImage };
        db.PhysicalBuildings.Add(pb);
        db.Buildings.Add(building);
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return (building.Id, room.Id);
    }

    private static async Task<Building> ReloadBuildingAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Buildings.AsNoTracking().SingleAsync(b => b.Id == id);
    }

    private static async Task<Room> ReloadRoomAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Rooms.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    [Fact]
    public async Task Replacing_a_building_image_saves_the_new_one_and_deletes_the_old()
    {
        var provider = Services();
        var images = provider.GetRequiredService<FakeImageStorage>();
        var (oldName, newName) = (NewName(), NewName());
        var (buildingId, _) = await SeedAsync(provider, oldName, null);
        images.Stored[newName] = [];

        using (var scope = provider.CreateScope())
        {
            var input = await ReloadBuildingAsync(provider, buildingId);
            input.ImageName = newName;
            var result = await scope.ServiceProvider.GetRequiredService<BuildingService>().TryUpdateAsync(input);
            Assert.True(result.Succeeded);
        }

        Assert.Equal(newName, (await ReloadBuildingAsync(provider, buildingId)).ImageName);
        Assert.Contains(oldName, images.Deleted);
        Assert.DoesNotContain(newName, images.Deleted);
    }

    [Fact]
    public async Task Clearing_a_room_image_deletes_it()
    {
        var provider = Services();
        var images = provider.GetRequiredService<FakeImageStorage>();
        var name = NewName();
        var (_, roomId) = await SeedAsync(provider, null, name);

        using (var scope = provider.CreateScope())
        {
            var input = await ReloadRoomAsync(provider, roomId);
            input.ImageName = null; // what "Ta bort bild" then Spara does
            await scope.ServiceProvider.GetRequiredService<RoomService>().UpdateAsync(input);
        }

        Assert.Null((await ReloadRoomAsync(provider, roomId)).ImageName);
        Assert.Contains(name, images.Deleted);
        Assert.False(images.Stored.ContainsKey(name), "the cleared image is still in storage");
    }

    [Fact]
    public async Task Replacing_a_room_image_saves_the_new_one_and_deletes_the_old()
    {
        var provider = Services();
        var images = provider.GetRequiredService<FakeImageStorage>();
        var (oldName, newName) = (NewName(), NewName());
        var (_, roomId) = await SeedAsync(provider, null, oldName);
        images.Stored[newName] = [];

        using (var scope = provider.CreateScope())
        {
            var input = await ReloadRoomAsync(provider, roomId);
            input.ImageName = newName;
            await scope.ServiceProvider.GetRequiredService<RoomService>().UpdateAsync(input);
        }

        Assert.Equal(newName, (await ReloadRoomAsync(provider, roomId)).ImageName);
        Assert.Contains(oldName, images.Deleted);
    }

    [Fact]
    public async Task Unrelated_edits_delete_no_image()
    {
        // The obvious way to get this wrong: delete on every update, wiping the photo when the admin
        // only renamed the house or changed a bed count.
        var provider = Services();
        var images = provider.GetRequiredService<FakeImageStorage>();
        var (buildingImage, roomImage) = (NewName(), NewName());
        var (buildingId, roomId) = await SeedAsync(provider, buildingImage, roomImage);

        using (var scope = provider.CreateScope())
        {
            var building = await ReloadBuildingAsync(provider, buildingId);
            building.Name = $"Omdöpt-{Guid.NewGuid():N}";
            await scope.ServiceProvider.GetRequiredService<BuildingService>().TryUpdateAsync(building);

            var room = await ReloadRoomAsync(provider, roomId);
            room.BedCount = 5;
            await scope.ServiceProvider.GetRequiredService<RoomService>().UpdateAsync(room);
        }

        Assert.DoesNotContain(buildingImage, images.Deleted);
        Assert.DoesNotContain(roomImage, images.Deleted);
        Assert.Equal(buildingImage, (await ReloadBuildingAsync(provider, buildingId)).ImageName);
        Assert.Equal(roomImage, (await ReloadRoomAsync(provider, roomId)).ImageName);
    }

    [Fact]
    public async Task Hard_deleting_a_room_then_its_building_deletes_both_images()
    {
        // Nothing can reference an image once its record is gone. The building can only be deleted
        // once its last room is (FR-16), so the two are deleted in that order.
        var provider = Services();
        var images = provider.GetRequiredService<FakeImageStorage>();
        var (buildingImage, roomImage) = (NewName(), NewName());
        var (buildingId, roomId) = await SeedAsync(provider, buildingImage, roomImage);

        using (var scope = provider.CreateScope())
        {
            Assert.Equal(HardDeleteResult.Deleted,
                await scope.ServiceProvider.GetRequiredService<RoomService>().TryHardDeleteAsync(roomId));
            Assert.Equal(HardDeleteResult.Deleted,
                await scope.ServiceProvider.GetRequiredService<BuildingService>().TryHardDeleteAsync(buildingId));
        }

        Assert.Contains(roomImage, images.Deleted);
        Assert.Contains(buildingImage, images.Deleted);
    }

    [Fact]
    public async Task A_storage_failure_does_not_fail_the_saved_edit()
    {
        // The edit is committed before cleanup runs; a storage hiccup must leave an orphan, not throw
        // away the admin's work.
        var provider = Services(); // its own FakeImageStorage, so DeleteThrows reaches no other test
        var images = provider.GetRequiredService<FakeImageStorage>();
        var name = NewName();
        var (buildingId, _) = await SeedAsync(provider, name, null);
        images.DeleteThrows = new InvalidOperationException("storage unavailable");

        using (var scope = provider.CreateScope())
        {
            var input = await ReloadBuildingAsync(provider, buildingId);
            input.ImageName = null;
            var result = await scope.ServiceProvider.GetRequiredService<BuildingService>().TryUpdateAsync(input); // must NOT throw
            Assert.True(result.Succeeded);
        }

        Assert.Null((await ReloadBuildingAsync(provider, buildingId)).ImageName);
    }
}
