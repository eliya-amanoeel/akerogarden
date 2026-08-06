using ClosedXML.Excel;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Renders the two admin download lists (FR-44/45) as an <c>.xlsx</c> workbook via ClosedXML — the
/// editable counterpart to the PDF (staff can sort/filter/annotate). Built off the shaped
/// <see cref="BookingListData"/> (snapshot values); within the NFR-10 target for 300 participants.
///
/// <para>The Deltagarlista (FR-44) is one sheet (named roster). The Matlista (FR-45) is a two-sheet
/// kitchen prep book: <b>Tillagning</b> (per-day/per-meal dish counts, no names) and <b>Kostbehov</b>
/// (only the people who need adapted plates, named, with their meals).</para>
/// </summary>
public static class ListExcelGenerator
{
    private static readonly string[] ParticipantHeaders = ["Namn", "Typ", "Ålder", "Närvaro", "Kostbehov"];
    private static readonly string[] PrepHeaders = ["Dag", "Måltid", "Rätt", "Antal"];
    private static readonly string[] SpecialDietHeaders = ["Namn", "Typ", "Kostbehov", "Måltider"];

    public static byte[] ParticipantList(BookingListData data)
    {
        using var workbook = new XLWorkbook();
        WriteSheet(workbook, "Deltagarlista", "Deltagarlista", data, ParticipantHeaders,
            data.Participants.Select(p => new[] { p.Name, p.Type, p.Age, p.Attendance, p.Dietary }));
        return Save(workbook);
    }

    public static byte[] FoodPreferenceList(BookingListData data)
    {
        using var workbook = new XLWorkbook();

        // Sheet 1 — kitchen prep summary: dish counts per meal per day (no names).
        WriteSheet(workbook, "Tillagning", "Matlista – tillagningsöversikt", data, PrepHeaders,
            data.Prep.Count == 0
                ? [["Inga måltider anmälda.", "", "", ""]]
                : data.Prep.Select(r => new[] { r.Day, r.Meal, r.Option, r.Count.ToString() }));

        // Sheet 2 — special-diet handling: only the people who need it, named, with their meals.
        WriteSheet(workbook, "Kostbehov", "Kostbehov & allergier", data, SpecialDietHeaders,
            data.SpecialDiets.Count == 0
                ? [["Inga särskilda kostbehov anmälda.", "", "", ""]]
                : data.SpecialDiets.Select(s => new[] { s.Name, s.Type, s.Dietary, s.Meals }),
            note: data.SpecialDiets.Count == 0 ? null : ListShaper.SpecialDietNote);

        return Save(workbook);
    }

    private static void WriteSheet(
        XLWorkbook workbook, string sheetName, string title, BookingListData data,
        string[] headers, IEnumerable<string[]> rows, string? note = null)
    {
        var ws = workbook.Worksheets.Add(sheetName);

        ws.Cell(1, 1).Value = $"{title} – {data.BookingReference} – {data.ContactName}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Incheckning {Fmt(data.CheckIn)} – Utcheckning {Fmt(data.CheckOut)}";
        if (note is not null)
        {
            ws.Cell(3, 1).Value = note;
            ws.Cell(3, 1).Style.Font.Italic = true;
        }

        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        var r = headerRow + 1;
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length; i++)
                ws.Cell(r, i + 1).Value = row[i];
            r++;
        }
        var lastRow = r - 1;

        // A frozen header + autofilter make the list usable for the kitchen/coordinators.
        ws.SheetView.FreezeRows(headerRow);
        ws.Range(headerRow, 1, headerRow, headers.Length).SetAutoFilter();

        // The per-person "Måltider" cell can hold multiple lines ("Tis 21/7: …\nOns 22/7: …") — wrap
        // so all show, and top-align the body. Harmless for the single-line sheets.
        if (lastRow >= headerRow + 1)
        {
            var body = ws.Range(headerRow + 1, 1, lastRow, headers.Length).Style;
            body.Alignment.WrapText = true;
            body.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        }
        ws.Columns().AdjustToContents();
        ws.Rows().AdjustToContents();
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string Fmt(DateOnly? date) => date?.ToString("yyyy-MM-dd") ?? "—";
}
