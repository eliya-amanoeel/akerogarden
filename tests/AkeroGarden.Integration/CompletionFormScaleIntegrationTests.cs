extern alias PublicApp;

using System.Text.RegularExpressions;
using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AkeroGarden.Integration;

/// <summary>
/// The completion form at the size it promises (§4.2, <see cref="CompletionFormModel.MaxParticipants"/>):
/// a booking with that many participants, overnight and day guests together, posts and comes back
/// whole — typed or filled from a file (FR-118), it is the same post.
///
/// <para>Written because the framework's defaults capped the form at 1,024 posted values — about 200
/// day guests — while the requirements said day guests were unbounded. Nothing showed the limit until
/// a post failed. This test posts the real form, through the real host, at the stated bound.</para>
/// </summary>
public sealed class CompletionFormScaleIntegrationTests : IClassFixture<DbFixture>, IDisposable
{
    private readonly DbFixture _db;
    private readonly WebApplicationFactory<PublicApp::Program> _public;

    public CompletionFormScaleIntegrationTests(DbFixture db)
    {
        _db = db;
        _public = new WebApplicationFactory<PublicApp::Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(Environments.Development);
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            b.UseSetting("Site:PublicBaseUrl", "https://akerogarden.se");
        });
    }

    public void Dispose() => _public.Dispose();

    [Fact]
    public async Task A_form_with_the_maximum_number_of_participants_posts_and_comes_back_whole()
    {
        const int overnight = 2;
        const int dayGuests = CompletionFormModel.MaxParticipants - overnight;

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var token = await ApprovedFullBoardBookingAsync(overnight, checkIn);
        using var client = _public.CreateClient();

        var page = await client.GetStringAsync($"/slutfor/{token}");
        var antiforgery = Regex.Match(page, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(antiforgery), "no antiforgery token in the rendered completion form");
        var handler = Regex.Match(page, @"name=""_handler""[^>]*value=""([^""]+)""").Groups[1].Value;

        // Multipart, as the browser sends it since FR-118. Every day guest carries every allergy — the
        // most values a row can post — so this is the heaviest form the bound allows.
        using var content = new MultipartFormDataContent();
        void Add(string name, string value) => content.Add(new StringContent(value), name);

        Add("__RequestVerificationToken", antiforgery);
        if (!string.IsNullOrEmpty(handler))
            Add("_handler", handler);
        for (var i = 0; i < overnight; i++)
        {
            Add($"Form.Overnight[{i}].FullName", $"Övernattande {i + 1}");
            Add($"Form.Overnight[{i}].AgeCategory", nameof(AgeCategory.Adult));
            Add($"Form.Overnight[{i}].AllergyNotes", "");
        }
        var day = checkIn.ToString("yyyy-MM-dd");
        for (var j = 0; j < dayGuests; j++)
        {
            Add($"Form.DayGuests[{j}].FullName", $"Dagsgäst nummer {j + 1}");
            Add($"Form.DayGuests[{j}].AgeCategory", nameof(AgeCategory.Adult));
            Add($"Form.DayGuests[{j}].AttendanceDate", day);
            foreach (var allergy in new[] { Allergy.Gluten, Allergy.Lactose, Allergy.Nut, Allergy.Other })
                Add($"Form.DayGuests[{j}].Allergies", allergy.ToString());
            Add($"Form.DayGuests[{j}].AllergyNotes", "sesam");
        }
        // A redraw, not a submit: it does not consume the link, and the whole roster must come back.
        Add("Action", "add-dayguest");

        using var response = await client.PostAsync($"/slutfor/{token}", content);
        // Decoded: Blazor HTML-encodes non-ASCII, so "Dagsgäst" arrives as an entity.
        var body = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.True(response.IsSuccessStatusCode, $"The form at its bound failed: {(int)response.StatusCode}");
        Assert.Contains($"aria-label=\"Dagsgäst {dayGuests}\"", body, StringComparison.Ordinal);
        Assert.Contains($"aria-label=\"Dagsgäst {dayGuests + 1}\"", body, StringComparison.Ordinal);
        Assert.Contains($"value=\"Dagsgäst nummer {dayGuests}\"", body, StringComparison.Ordinal);
    }

    private async Task<string> ApprovedFullBoardBookingAsync(int overnight, DateOnly checkIn)
    {
        var token = $"scale-{Guid.NewGuid():N}";

        using var scope = _db.BuildServices().CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ctx.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = $"AG-S-{Guid.NewGuid():N}"[..20],
            BookingType = BookingType.StayRequest,
            Status = BookingStatus.Approved,
            ContactName = "Stor Grupp",
            ContactEmail = "stor@test.se",
            CheckIn = checkIn,
            CheckOut = checkIn.AddDays(2),
            OvernightGuestCount = overnight,
            MealPlan = MealPlan.FullBoardAll,
            CompletionToken = token,
            CompletionTokenExpiry = DateTime.UtcNow.AddDays(14),
            CompletionLinkSentAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            DeleteAfter = checkIn.AddYears(1),
        });
        await ctx.SaveChangesAsync();
        return token;
    }
}
