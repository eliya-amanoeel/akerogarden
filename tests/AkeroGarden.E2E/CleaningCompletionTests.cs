using AkeroGarden.E2E.Infrastructure;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The completion form's cleaning self-clean/buy toggles (Phase 8 cleaning rework, §5.5, FR-93):
/// Huvudbyggnad and Sovbyggnad(er) always render, Konferensbyggnad only when the Konferensrum
/// facility was actually booked, and a real submit persists the choices to the database — the
/// server-authoritative check honors Huvudbyggnad/Konferensbyggnad but the sleeping toggle's
/// intent-only nature (no room assignment on this fixture) is exercised in the integration suite.
/// </summary>
public class CleaningCompletionTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private static string Url(string token) => $"/slutfor/{token}";

    [Fact]
    public async Task Default_booking_shows_main_and_sleeping_toggles_but_not_conference()
    {
        await Page.GotoAsync(Url(E2EFixture.CompletionToken));

        await Assertions.Expect(Page.Locator("#clean-main")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#clean-sleeping")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#clean-conference")).ToHaveCountAsync(0);

        // Default is self-clean (§5.5) — neither toggle starts checked.
        await Assertions.Expect(Page.Locator("#clean-main")).Not.ToBeCheckedAsync();
        await Assertions.Expect(Page.Locator("#clean-sleeping")).Not.ToBeCheckedAsync();
    }

    [Fact]
    public async Task Booking_with_conference_facility_also_shows_the_conference_toggle()
    {
        await Page.GotoAsync(Url(E2EFixture.ConferenceCompletionToken));

        await Assertions.Expect(Page.Locator("#clean-main")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#clean-sleeping")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#clean-conference")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#clean-conference")).Not.ToBeCheckedAsync();
    }

    [Fact]
    public async Task Completion_submit_persists_the_chosen_cleaning_selections_to_the_database()
    {
        await Page.GotoAsync(Url(E2EFixture.ConferenceCompletionSubmitToken));

        await Page.Locator(".prow[aria-label='Deltagare 1'] .prow__name").FillAsync("Anna Andersson");
        await Page.Locator(".prow[aria-label='Deltagare 2'] .prow__name").FillAsync("Bengt Berg");
        await SelectEveryGroupDefaultLunchAsync("Ugnsbakad lax");

        await Page.Locator("#clean-main").CheckAsync();
        await Page.Locator("#clean-conference").CheckAsync();
        // Sleeping deliberately left unchecked — exercises the default self-clean path too.

        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka in uppgifter" }).ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();

        // Honest verification: drive the real behaviour end-to-end (UI → server → DB), not just the
        // "Tack!" page — that only proves the post succeeded, not that the choices were honored.
        await using var db = Fixture.NewDbContext();
        var booking = await db.Bookings
            .Include(b => b.CleaningSelections)
            .SingleAsync(b => b.CompletionToken == E2EFixture.ConferenceCompletionSubmitToken);

        Assert.True(booking.MainBuildingCleaningPurchased);
        Assert.True(booking.ConferenceCleaningPurchased);
        Assert.False(booking.SleepingCleaningPurchased);

        Assert.Contains(booking.CleaningSelections, s => s.ZoneNameSnapshot == "Huvudbyggnad");
        Assert.Contains(booking.CleaningSelections, s => s.ZoneNameSnapshot == "Konferensbyggnad");
        Assert.Equal(2, booking.CleaningSelections.Count); // exactly the two chosen — no sleeping row
    }

    /// <summary>Selects the given option on every per-day group-default lunch dropdown, cascading it
    /// to that day's slot for all participants (mirrors CompletionTests' helper).</summary>
    private async Task SelectEveryGroupDefaultLunchAsync(string label)
    {
        var selects = Page.Locator("select[data-groupdefault-slot][data-meal='Lunch']");
        var count = await selects.CountAsync();
        for (var i = 0; i < count; i++)
            await selects.Nth(i).SelectOptionAsync(new SelectOptionValue { Label = label });
    }
}
