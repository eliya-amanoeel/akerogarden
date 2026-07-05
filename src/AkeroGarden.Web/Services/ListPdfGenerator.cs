using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Renders the two admin download lists (FR-44/45) as PDF via QuestPDF. Built off the shaped
/// <see cref="BookingListData"/> (snapshot values), comfortably within the NFR-10 target
/// (≤10 s for 300 participants). QuestPDF's Community licence is enabled at startup in Program.cs.
/// </summary>
public static class ListPdfGenerator
{
    static ListPdfGenerator()
    {
        // QuestPDF Community licence (free below the revenue threshold) — must be set before any
        // PDF is generated. Setting it here guarantees it wherever the generator runs (app + tests).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static readonly string[] ParticipantHeaders = ["Namn", "Typ", "Ålder", "Rum", "Närvaro", "Kostbehov"];
    private static readonly string[] FoodHeaders = ["Namn", "Typ", "Helpension", "Frukost", "Måltidsval", "Kostbehov"];

    public static byte[] ParticipantList(BookingListData data) =>
        Build("Deltagarlista", data, ParticipantHeaders,
            data.Participants.Select(p => new[] { p.Name, p.Type, p.Age, p.Room, p.Attendance, p.Dietary }));

    public static byte[] FoodPreferenceList(BookingListData data) =>
        Build("Matlista", data, FoodHeaders,
            data.Food.Select(f => new[] { f.Name, f.Type, f.FullBoard, f.Breakfast, f.Meals, f.Dietary }));

    private static byte[] Build(string title, BookingListData data, string[] headers, IEnumerable<string[]> rows)
    {
        var rowList = rows.ToList();
        var subtitle = $"{data.ContactName} · Incheckning {Fmt(data.CheckIn)} – Utcheckning {Fmt(data.CheckOut)}";

        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(25);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Column(col =>
            {
                col.Item().Text(title).Bold().FontSize(15);
                col.Item().Text(subtitle).FontSize(9).FontColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingVertical(8).Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    // Give the free-text columns (Room/Meals + Dietary) more width.
                    foreach (var header in headers)
                        cd.RelativeColumn(header is "Måltidsval" ? 3 : header is "Kostbehov" or "Rum" ? 2 : 1);
                });

                table.Header(h =>
                {
                    foreach (var head in headers)
                        h.Cell().Element(HeaderCell).Text(head).Bold();
                });

                foreach (var row in rowList)
                    foreach (var cell in row)
                        table.Cell().Element(BodyCell).Text(cell);
            });

            page.Footer().AlignRight().Text(t =>
            {
                t.Span($"Åkerögården · {rowList.Count} rader · ");
                t.Span($"{DateTime.Now:yyyy-MM-dd}");
            });
        }));

        return document.GeneratePdf();
    }

    private static string Fmt(DateOnly? date) => date?.ToString("yyyy-MM-dd") ?? "—";

    private static IContainer HeaderCell(IContainer c) =>
        c.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4);

    private static IContainer BodyCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4);
}
