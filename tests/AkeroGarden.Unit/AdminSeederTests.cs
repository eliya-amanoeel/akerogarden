using AkeroGarden.Web.Data;
using AkeroGarden.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AkeroGarden.Unit;

public class AdminSeederTests
{
    private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock(
        IEnumerable<ApplicationUser> existingUsers)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr = new Mock<UserManager<ApplicationUser>>(
            store.Object, null, null, null, null, null, null, null, null);
        mgr.Setup(m => m.Users).Returns(existingUsers.AsQueryable());
        return mgr;
    }

    [Fact]
    public async Task SeedAsync_DoesNothing_WhenUsersExist()
    {
        var mgr = CreateUserManagerMock([new ApplicationUser { Email = "existing@test.se" }]);
        var config = new ConfigurationBuilder().Build();
        var seeder = new AdminSeeder(mgr.Object, config, NullLogger<AdminSeeder>.Instance);

        await seeder.SeedAsync();

        mgr.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SeedAsync_DoesNotCreateUser_WhenConfigMissing()
    {
        var mgr = CreateUserManagerMock([]);
        var config = new ConfigurationBuilder().Build();
        var seeder = new AdminSeeder(mgr.Object, config, NullLogger<AdminSeeder>.Instance);

        await seeder.SeedAsync();

        mgr.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SeedAsync_CreatesUser_WhenNoUsersAndConfigPresent()
    {
        var mgr = CreateUserManagerMock([]);
        mgr.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
           .ReturnsAsync(IdentityResult.Success);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:AdminEmail"] = "admin@akerogarden.se",
                ["Seed:AdminPassword"] = "TestLösenord!23"
            })
            .Build();

        var seeder = new AdminSeeder(mgr.Object, config, NullLogger<AdminSeeder>.Instance);
        await seeder.SeedAsync();

        mgr.Verify(m => m.CreateAsync(
            It.Is<ApplicationUser>(u => u.Email == "admin@akerogarden.se"),
            "TestLösenord!23"),
            Times.Once);
    }
}
