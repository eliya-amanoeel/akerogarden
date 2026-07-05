using ClosedXML.Excel;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Renders the two admin download lists (FR-44/45) as an <c>.xlsx</c> workbook via ClosedXML — the
/// editable counterpart to the PDF (staff can sort/filter/annotate). Built off the shaped
/// <see cref="BookingListData"/> (snapshot values); within the NFR-10 target for 300 participants.
/// </summary>
public static class ListExcelGenerator
{
    private static readonly string[] ParticipantHeaders = ["Namn", "Typ", "Ålder", "Rum", "Närvaro", "Kostbehov"];
    private static readonly string[] FoodHeaders = ["Namn", "Typ", "Helpension", "Frukost", "Måltidsval", "Kostbehov"];

    public static byte[] ParticipantList(BookingListData data) =>
        Build("Deltagarlista", data, ParticipantHeaders,
            data.Participants.Select(p => new[] { p.Name, p.Type, p.Age, p.Room, p.Attendance, p.Dietary }));

    public static byte[] FoodPreferenceList(BookingListData data) =>
        Build("Matlista", data, FoodHeaders,
            data.Food.Select(f => new[] { f.Name, f.Type, f.FullBoard, f.Breakfast, f.Meals, f.Dietary }));

    private static byte[] Build(string sheetName, BookingListData data, string[] headers, IEnumerable<string[]> rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(sheetName);

        ws.Cell(1, 1).Value = $"{sheetName} – {data.ContactName}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Incheckning {Fmt(data.CheckIn)} – Utcheckning {Fmt(data.CheckOut)}";

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

        // A frozen header + autofilter make the list usable for the kitchen/coordinators.
        ws.SheetView.FreezeRows(headerRow);
        ws.Range(headerRow, 1, headerRow, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string Fmt(DateOnly? date) => date?.ToString("yyyy-MM-dd") ?? "—";
}
