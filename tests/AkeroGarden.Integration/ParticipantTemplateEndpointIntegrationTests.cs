extern alias PublicApp;

using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Integration;

/// <summary>
/// The participant template download, <c>/slutfor/{token}/mall.xlsx</c> (FR-118), as the public host
/// actually serves it: gated by the same token check as the completion form, generated for that
/// booking, and never cached.
/// </summary>
public sealed class ParticipantTemplateEndpointIntegrationTests : IClassFixture<DbFixture>, IDisposable
{
    private readonly DbFixture _db;
    private readonly WebApplicationFactory<PublicApp::Program> _public;

    public ParticipantTemplateEndpointIntegrationTests(DbFixture db)
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

    /// <summary>An approved full-board booking with a live link. The host runs on the real clock, so
    /// the dates are relative to it and far from any boundary: the link is valid by days, not
    /// minutes.</summary>
    private async Task<string> ApprovedBookingAsync(BookingStatus status = BookingStatus.Approved)
    {
        var token = $"template-{Guid.NewGuid():N}";
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        using var scope = _db.BuildServices().CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ctx.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = $"AG-T-{Guid.NewGuid():N}"[..20],
            BookingType = BookingType.StayRequest,
            Status = status,
            ContactName = "Mall Testsson",
            ContactEmail = "mall@test.se",
            CheckIn = checkIn,
            CheckOut = checkIn.AddDays(2),
            OvernightGuestCount = 5,
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

    [Fact]
    public async Task A_live_link_gets_this_bookings_template_uncached()
    {
        var token = await ApprovedBookingAsync();
        using var client = _public.CreateClient();

        using var response = await client.GetAsync($"/slutfor/{token}/mall.xlsx");

        response.EnsureSuccessStatusCode();
        Assert.Equal(ParticipantTemplate.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ParticipantTemplate.FileName, response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);

        // Shaped by the booking: five overnight rows, and a day-guest sheet because it is full board.
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var overnight = workbook.Worksheet(ParticipantTemplate.OvernightSheet);
        Assert.Equal("5", overnight.Cell(ParticipantTemplate.HeaderRow + 5, ParticipantTemplate.NumberColumn).GetString());
        Assert.Equal("", overnight.Cell(ParticipantTemplate.HeaderRow + 6, ParticipantTemplate.NumberColumn).GetString());
        Assert.True(workbook.Worksheets.Contains(ParticipantTemplate.DayGuestSheet));
    }

    [Fact]
    public void An_accepted_upload_is_buffered_in_memory_never_on_disk()
    {
        // The participant file holds health data. ASP.NET writes an uploaded file above
        // MemoryBufferThreshold (64 KB by default) to a temporary file; the public host raises it to
        // the upload limit so that no file the form accepts ever touches disk (FR-118).
        var form = _public.Services.GetRequiredService<IOptions<FormOptions>>().Value;

        Assert.True(form.MemoryBufferThreshold >= ParticipantImport.MaxFileBytes,
            $"MemoryBufferThreshold is {form.MemoryBufferThreshold} — a file up to {ParticipantImport.MaxFileBytes} bytes would spill to disk.");
    }

    [Fact]
    public async Task An_unknown_token_gets_no_template()
    {
        using var client = _public.CreateClient();

        using var response = await client.GetAsync("/slutfor/not-a-real-token/mall.xlsx");

        Assert.NotEqual(ParticipantTemplate.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_used_link_gets_no_template()
    {
        // Once the details are in (DetailsComplete) the form no longer opens, and neither does this.
        var token = await ApprovedBookingAsync(BookingStatus.DetailsComplete);
        using var client = _public.CreateClient();

        using var response = await client.GetAsync($"/slutfor/{token}/mall.xlsx");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
