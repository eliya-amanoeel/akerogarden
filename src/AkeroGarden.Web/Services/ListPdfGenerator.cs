using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Renders the two admin download lists (FR-44/45) as PDF via QuestPDF. Built off the shaped
/// <see cref="BookingListData"/> (snapshot values), comfortably within the NFR-10 target
/// (≤10 s for 300 participants). QuestPDF's Community licence is enabled at startup in Program.cs.
///
/// <para>The Deltagarlista (FR-44) is a plain named roster table. The Matlista (FR-45) is a kitchen
/// prep sheet: a "Tillagningsöversikt" table of per-day/per-meal dish counts (no names), then a
/// "Kostbehov &amp; allergier" table naming only the people who need adapted plates.</para>
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

    public static byte[] ParticipantList(BookingListData data) =>
        Build("Deltagarlista", data, ParticipantHeaders,
            data.Participants.Select(p => new[] { p.Name, p.Type, p.Age, p.Room, p.Attendance, p.Dietary }),
            ParticipantHeaders.Select(h => h is "Kostbehov" or "Rum" ? 2f : 1f).ToArray());

    public static byte[] FoodPreferenceList(BookingListData data)
    {
        var subtitle = $"{data.ContactName} · Incheckning {Fmt(data.CheckIn)} – Utcheckning {Fmt(data.CheckOut)}";

        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(25);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Column(col =>
            {
                col.Item().Text("Matlista").Bold().FontSize(15);
                col.Item().Text(subtitle).FontSize(9).FontColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingVertical(8).Column(col =>
            {
                // ── Section 1: kitchen prep summary (dish counts per meal per day, no names) ──
                col.Item().Text("Tillagningsöversikt").Bold().FontSize(12);
                col.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(1.4f); // Dag
                        cd.RelativeColumn(1.2f); // Måltid
                        cd.RelativeColumn(4f);   // Rätt
                        cd.RelativeColumn(0.8f); // Antal
                    });
                    table.Header(h =>
                    {
                        foreach (var head in new[] { "Dag", "Måltid", "Rätt", "Antal" })
                            h.Cell().Element(HeaderCell).Text(head).Bold();
                    });
                    if (data.Prep.Count == 0)
                    {
                        table.Cell().ColumnSpan(4).Element(BodyCell).Text("Inga måltider anmälda.");
                    }
                    else
                    {
                        foreach (var r in data.Prep)
                        {
                            table.Cell().Element(BodyCell).Text(r.Day);
                            table.Cell().Element(BodyCell).Text(r.Meal);
                            table.Cell().Element(BodyCell).Text(r.Option);
                            table.Cell().Element(BodyCell).Text(r.Count.ToString());
                        }
                    }
                });

                // ── Section 2: named special-diet handling ──
                col.Item().PaddingTop(16).Text("Kostbehov & allergier").Bold().FontSize(12);
                if (data.SpecialDiets.Count == 0)
                {
                    col.Item().PaddingTop(4).Text("Inga särskilda kostbehov anmälda.").FontColor(Colors.Grey.Darken1);
                }
                else
                {
                    // The special-diet portions are already in the counts above — the kitchen adapts
                    // those portions, it does not cook extra ones. Spelling this out avoids double-counting.
                    col.Item().PaddingTop(4).Text(ListShaper.SpecialDietNote).Italic().FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(1.6f); // Namn
                            cd.RelativeColumn(1f);   // Typ
                            cd.RelativeColumn(2f);   // Kostbehov
                            cd.RelativeColumn(3.4f); // Måltider
                        });
                        table.Header(h =>
                        {
                            foreach (var head in new[] { "Namn", "Typ", "Kostbehov", "Måltider" })
                                h.Cell().Element(HeaderCell).Text(head).Bold();
                        });
                        foreach (var s in data.SpecialDiets)
                        {
                            table.Cell().Element(BodyCell).Text(s.Name);
                            table.Cell().Element(BodyCell).Text(s.Type);
                            table.Cell().Element(BodyCell).Text(s.Dietary);
                            table.Cell().Element(BodyCell).Text(s.Meals);
                        }
                    });
                }
            });

            page.Footer().AlignRight().Text(t =>
            {
                t.Span($"Åkerögården · {data.Prep.Count} rätter · {data.SpecialDiets.Count} kostbehov · ");
                t.Span($"{DateTime.Now:yyyy-MM-dd}");
            });
        }));

        return document.GeneratePdf();
    }

    private static byte[] Build(string title, BookingListData data, string[] headers, IEnumerable<string[]> rows, float[] widths)
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
                    foreach (var w in widths)
                        cd.RelativeColumn(w);
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
