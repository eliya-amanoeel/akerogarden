using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for PriceRule reference data against real PostgreSQL
/// (FR-31–34): create with decimal precision round-trip, soft-deactivate, and the
/// always-allowed hard-delete (no referencing entity exists — FR-34, erd.md).
/// Tests use unique labels because the fixture's database is shared across the class.
/// </summary>
public class PriceRuleIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public PriceRuleIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private PriceRuleService NewService(out IServiceScope scope)
    {
        var services = _fixture.BuildServices(s => s.AddScoped<PriceRuleService>());
        scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PriceRuleService>();
    }

    [Fact]
    public async Task Create_RoundTripsDecimalsAndSeason()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var rule = new PriceRule
        {
            Label = $"Helpension-{Guid.NewGuid():N}",
            AmountExVat = 550.00m,
            VatRate = 0.12m,
            Unit = "per person och natt",
            SeasonStart = new DateOnly(2026, 4, 1),
            SeasonEnd = new DateOnly(2026, 9, 30)
        };
        await service.CreateAsync(rule);

        var reloaded = await service.GetAsync(rule.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(550.00m, reloaded!.AmountExVat);
        Assert.Equal(0.12m, reloaded.VatRate);
        Assert.Equal(new DateOnly(2026, 4, 1), reloaded.SeasonStart);
        Assert.Equal(new DateOnly(2026, 9, 30), reloaded.SeasonEnd);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task Deactivate_KeepsRecord_AndFlipsFlag()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var rule = new PriceRule { Label = $"Stuga-{Guid.NewGuid():N}", AmountExVat = 1500m, VatRate = 0.12m, Unit = "per natt" };
        await service.CreateAsync(rule);

        await service.SetActiveAsync(rule.Id, false);

        var reloaded = await service.GetAsync(rule.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task Delete_RemovesRecord()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var rule = new PriceRule { Label = $"Tält-{Guid.NewGuid():N}", AmountExVat = 250m, VatRate = 0.25m, Unit = "per dag" };
        await service.CreateAsync(rule);

        await service.DeleteAsync(rule.Id);

        Assert.Null(await service.GetAsync(rule.Id));
    }
}
