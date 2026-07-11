using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Models;
using AkeroGarden.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for guest-side room assignment against real PostgreSQL (Phase 4 slice 5,
/// FR-56, §3.3): available rooms exclude blocked ones; a completion submit with a chosen room
/// persists the live <see cref="Participant.RoomId"/> + a snapshot; a choice that is not available
/// (blocked) is dropped server-side. (The admin toggle that sets <c>GuestChoosesRooms</c> is
/// slice 6 — here it is set directly.)
/// </summary>
public class RoomAssignmentIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public RoomAssignmentIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider() =>
        _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(new FakeEmailService());
            s.AddScoped<BookingEmailService>();
            s.AddScoped<RoomAvailabilityService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
            s.AddScoped<BuildingService>();
            s.AddScoped<RoomService>();
            s.AddScoped<BlockedPeriodService>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
        });

    private sealed record Setup(Guid BookingId, string Token, Guid RoomId, string BuildingName, string RoomName);

    private static async Task<Setup> SetupAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var buildings = scope.ServiceProvider.GetRequiredService<BuildingService>();
        var rooms = scope.ServiceProvider.GetRequiredService<RoomService>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        var physicalBuilding = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Lilla-{Guid.NewGuid():N}" };
        db.PhysicalBuildings.Add(physicalBuilding);
        await db.SaveChangesAsync();

        var building = new Building { Name = $"Blidö-{Guid.NewGuid():N}", PhysicalBuildingId = physicalBuilding.Id, DisplayOrder = 1 };
        await buildings.TryCreateAsync(building);
        var room = new Room { Name = "Rum 1", BedCount = 4, BuildingId = building.Id, DisplayOrder = 1 };
        await rooms.CreateAsync(room);

        var booking = await bookings.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Grupp",
            ContactEmail = $"grupp-{Guid.NewGuid():N}@test.se",
            CheckIn = new DateOnly(2026, 8, 10),
            CheckOut = new DateOnly(2026, 8, 12),
            OvernightGuestCount = 1,
            MealPlan = MealPlan.SelfCateringAll,
            AcceptPrivacyPolicy = true
        }, createdByAdmin: true);
        await bookings.ApproveAsync(booking.Id);

        // Slice-6 admin toggle stands in here: let the guest choose rooms.
        var persisted = await db.Bookings.FindAsync(booking.Id);
        persisted!.GuestChoosesRooms = true;
        await db.SaveChangesAsync();

        return new Setup(booking.Id, persisted.CompletionToken!, room.Id, building.Name, room.Name);
    }

    private static CompletionFormModel FormWithRoom(string? roomChoice) =>
        new()
        {
            Overnight =
            [
                new OvernightParticipantEntry
                {
                    FullName = "Deltagare",
                    AgeCategory = AgeCategory.Adult,
                    IsFullBoard = false,
                    RoomChoice = roomChoice,
                }
            ]
        };

    [Fact]
    public async Task AvailableRooms_IncludesRoom_ThenExcludesItWhenBlocked()
    {
        var provider = NewProvider();
        var setup = await SetupAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var availability = scope.ServiceProvider.GetRequiredService<RoomAvailabilityService>();
            var available = await availability.GetAvailableRoomsAsync(setup.BookingId);
            Assert.Contains(available, r => r.Id == setup.RoomId);
        }

        // Block the room over the stay.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings.FindAsync(setup.BookingId);
            db.BlockedPeriods.Add(new BlockedPeriod
            {
                Id = Guid.NewGuid(),
                Scope = BlockScope.Room,
                BuildingId = (await db.Rooms.FindAsync(setup.RoomId))!.BuildingId,
                RoomId = setup.RoomId,
                StartDate = booking!.CheckIn!.Value,
                EndDate = booking.CheckOut!.Value,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var availability = scope.ServiceProvider.GetRequiredService<RoomAvailabilityService>();
            var available = await availability.GetAvailableRoomsAsync(setup.BookingId);
            Assert.DoesNotContain(available, r => r.Id == setup.RoomId);
        }
    }

    [Fact]
    public async Task Submit_WithRoomChoice_PersistsRoomIdAndSnapshot()
    {
        var provider = NewProvider();
        var setup = await SetupAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var result = await service.SubmitCompletionAsync(
                setup.Token, FormWithRoom(setup.RoomId.ToString()), DateTime.UtcNow);
            Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var participant = await db.Participants.SingleAsync(p => p.BookingId == setup.BookingId);
            Assert.Equal(setup.RoomId, participant.RoomId);
            Assert.Equal($"{setup.RoomName} — {setup.BuildingName}", participant.RoomAssignmentSnapshot);
        }
    }

    [Fact]
    public async Task Submit_WithUnavailableRoomChoice_LeavesUnassigned()
    {
        var provider = NewProvider();
        var setup = await SetupAsync(provider);

        // Block the room so the chosen id is no longer available.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings.FindAsync(setup.BookingId);
            db.BlockedPeriods.Add(new BlockedPeriod
            {
                Id = Guid.NewGuid(),
                Scope = BlockScope.Room,
                BuildingId = (await db.Rooms.FindAsync(setup.RoomId))!.BuildingId,
                RoomId = setup.RoomId,
                StartDate = booking!.CheckIn!.Value,
                EndDate = booking.CheckOut!.Value,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.Equal(CompletionSubmitOutcome.Success,
                (await service.SubmitCompletionAsync(setup.Token, FormWithRoom(setup.RoomId.ToString()), DateTime.UtcNow)).Outcome);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var participant = await db.Participants.SingleAsync(p => p.BookingId == setup.BookingId);
            Assert.Null(participant.RoomId); // the blocked room was dropped server-side
            Assert.Null(participant.RoomAssignmentSnapshot);
        }
    }
}
