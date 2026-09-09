using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.DependencyInjection;

using AkeroGarden.Web.Core.Data;
using Microsoft.EntityFrameworkCore;
namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for Facility reference data against real PostgreSQL
/// (FR-27–30, NFR-14): create with image URL, soft-deactivate, and hard-delete
/// (no referents until Phase 3). Blob upload itself is an infrastructure adapter
/// (BlobImageStorage), verified in dev / Phase 6 — not exercised here.
/// Tests use unique names because the fixture's database is shared across the class.
/// </summary>
public class FacilityIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public FacilityIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private FacilityService NewService(out IServiceScope scope)
    {
        var services = _fixture.BuildServices(s => s.AddScoped<FacilityService>());
        scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<FacilityService>();
    }

    [Fact]
    public async Task Create_PersistsImageUrl_AndIsActive()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var url = $"http://127.0.0.1:10000/devstoreaccount1/facility-images/{Guid.NewGuid():N}.jpg";
        var facility = new Facility { Name = $"Kapellet-{Guid.NewGuid():N}", ImageUrl = url };
        await service.CreateAsync(facility);

        var reloaded = await service.GetAsync(facility.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(url, reloaded!.ImageUrl);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task Deactivate_KeepsRecord_AndFlipsFlag()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var facility = new Facility { Name = $"Spelrum-{Guid.NewGuid():N}" };
        await service.CreateAsync(facility);

        await service.SetActiveAsync(facility.Id, false);

        var reloaded = await service.GetAsync(facility.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task HardDelete_Succeeds_WhenUnreferenced()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var facility = new Facility { Name = $"Konferensrum-{Guid.NewGuid():N}" };
        await service.CreateAsync(facility);

        var result = await service.TryHardDeleteAsync(facility.Id);

        Assert.Equal(HardDeleteResult.Deleted, result);
        Assert.Null(await service.GetAsync(facility.Id));
    }

    // ── Image cleanup (raised by the maintainer 2026-09-09) ─────────────────────
    // The panel could only ever REPLACE an image, never remove one, and nothing in the app deleted a
    // blob at all — so every edit orphaned the previous file permanently, still fetchable by anyone
    // holding its name. FR-27 covers upload and never mentions removal.

    private static Facility NewFacility(string? image) => new()
    {
        Name = $"Kapellet-{Guid.NewGuid():N}",
        ImageUrl = image,
    };

    [Fact]
    public async Task ReplacingAnImage_DeletesTheOldBlob()
    {
        var provider = _fixture.BuildServices(s => s.AddScoped<FacilityService>());
        var images = provider.GetRequiredService<FakeImageStorage>();
        var oldName = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg";
        var newName = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.jpg";
        images.Stored[oldName] = [];
        images.Stored[newName] = [];

        Guid id;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var facility = NewFacility(oldName);
            db.Facilities.Add(facility);
            await db.SaveChangesAsync();
            id = facility.Id;
        }

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<FacilityService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var input = await db.Facilities.AsNoTracking().SingleAsync(f => f.Id == id);
            input.ImageUrl = newName;
            await service.UpdateAsync(input);
        }

        Assert.Contains(oldName, images.Deleted);
        Assert.DoesNotContain(newName, images.Deleted);
        Assert.False(images.Stored.ContainsKey(oldName), "the superseded blob is still in storage");
    }

    [Fact]
    public async Task ClearingAnImage_DeletesTheBlob()
    {
        var provider = _fixture.BuildServices(s => s.AddScoped<FacilityService>());
        var images = provider.GetRequiredService<FakeImageStorage>();
        var name = "cccccccccccccccccccccccccccccccc.png";
        images.Stored[name] = [];

        Guid id;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var facility = NewFacility(name);
            db.Facilities.Add(facility);
            await db.SaveChangesAsync();
            id = facility.Id;
        }

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<FacilityService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var input = await db.Facilities.AsNoTracking().SingleAsync(f => f.Id == id);
            input.ImageUrl = null; // what "Ta bort bild" then Save does
            await service.UpdateAsync(input);
        }

        Assert.Contains(name, images.Deleted);
        using var check = provider.CreateScope();
        var reloaded = await check.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Facilities.SingleAsync(f => f.Id == id);
        Assert.Null(reloaded.ImageUrl);
    }

    [Fact]
    public async Task SavingWithoutChangingTheImage_DeletesNothing()
    {
        // The obvious way to get this wrong: delete on every update, wiping the image on an unrelated
        // edit such as renaming the facility.
        var provider = _fixture.BuildServices(s => s.AddScoped<FacilityService>());
        var images = provider.GetRequiredService<FakeImageStorage>();
        var name = "dddddddddddddddddddddddddddddddd.webp";
        images.Stored[name] = [];

        Guid id;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var facility = NewFacility(name);
            db.Facilities.Add(facility);
            await db.SaveChangesAsync();
            id = facility.Id;
        }

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<FacilityService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var input = await db.Facilities.AsNoTracking().SingleAsync(f => f.Id == id);
            input.Name = "Omdöpt kapell";
            await service.UpdateAsync(input);
        }

        Assert.Empty(images.Deleted);
        Assert.True(images.Stored.ContainsKey(name), "an unrelated edit removed the image");
    }

    [Fact]
    public async Task AStorageFailure_DoesNotFailTheSavedEdit()
    {
        // The edit is already committed by the time cleanup runs. A storage hiccup must leave an
        // orphan, not throw away the admin's work.
        var provider = _fixture.BuildServices(s => s.AddScoped<FacilityService>());
        var images = provider.GetRequiredService<FakeImageStorage>();
        var oldName = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee.jpg";
        images.Stored[oldName] = [];
        images.DeleteThrows = new InvalidOperationException("storage unavailable");

        Guid id;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var facility = NewFacility(oldName);
            db.Facilities.Add(facility);
            await db.SaveChangesAsync();
            id = facility.Id;
        }

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<FacilityService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var input = await db.Facilities.AsNoTracking().SingleAsync(f => f.Id == id);
            input.ImageUrl = null;
            await service.UpdateAsync(input); // must NOT throw
        }

        using var check = provider.CreateScope();
        var reloaded = await check.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Facilities.SingleAsync(f => f.Id == id);
        Assert.Null(reloaded.ImageUrl);
    }
}
