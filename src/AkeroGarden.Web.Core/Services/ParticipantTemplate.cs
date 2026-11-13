using AkeroGarden.Web.Core.Data.Entities;
using ClosedXML.Excel;

namespace AkeroGarden.Web.Core.Services;

/// <summary>What a booking's participant template looks like: how many overnight rows, which days a
/// day guest can pick, and whether there is a day-guest sheet at all (§4.2 — only a full-board
/// booking has day guests). The template (<see cref="ParticipantTemplate"/>) is built from it and
/// the upload (<see cref="ParticipantImport"/>) is read against it, so the two cannot disagree.</summary>
public sealed record ParticipantTemplateShape(
    int OvernightCount, IReadOnlyList<DateOnly> StayDates, bool IncludesDayGuests)
{
    /// <summary>The shape of a booking's template. One source for the download endpoint and the
    /// form's upload, so the file a guest gets and the file the form reads cannot differ.</summary>
    public static ParticipantTemplateShape For(Booking booking)
    {
        var dates = new List<DateOnly>();
        if (booking is { CheckIn: { } checkIn, CheckOut: { } checkOut })
        {
            for (var d = checkIn; d <= checkOut; d = d.AddDays(1))
                dates.Add(d);
        }

        return new ParticipantTemplateShape(
            booking.OvernightGuestCount ?? 0, dates, InquiryNormalizer.DayGuestsApplicable(booking.MealPlan));
    }
}

/// <summary>
/// The Excel participant template a guest downloads from the completion form (FR-118), generated per
/// booking with ClosedXML — the same library as the admin lists (FR-44/45).
///
/// <para><b>Generated, not a static file</b>, so that the overnight sheet has exactly the booking's
/// number of rows, the day dropdown offers only the stay's own days, and every label comes from the
/// same place the form takes it (<see cref="BookingDisplay"/>) — a hand-kept file would drift from
/// both.</para>
///
/// <para><b>Everything inside the file is a convenience, never a control.</b> Dropdowns, the text
/// rule, the red empty cells and the locked layout help the guest fill it in; pasting overwrites
/// validation, other spreadsheet programs enforce it unevenly, and protection without a password
/// can be lifted by anyone. <see cref="ParticipantImport"/> re-checks all of it, and the submit
/// re-checks the result again (<see cref="CompletionNormalizer"/>). No macros: saving is never
/// blocked, and a macro-enabled file is refused on upload.</para>
/// </summary>
public static class ParticipantTemplate
{
    public const string OvernightSheet = "Deltagare";
    public const string DayGuestSheet = "Dagsgäster";

    /// <summary>The hidden sheet holding the dropdown values. A range reference rather than an
    /// inline list: Excel caps an inline list at 255 characters, which a long stay's day names
    /// would exceed.</summary>
    public const string ListsSheet = "Listor";

    /// <summary>The header row on both sheets; data starts on the row after it. Rows above hold the
    /// title and the instructions.</summary>
    public const int HeaderRow = 4;

    /// <summary>Pre-formatted day-guest rows. The sheet is locked, so this is the most a guest can
    /// enter in the file; a larger group uploads first and adds the rest on the form, up to the form's
    /// own bound (<see cref="Models.CompletionFormModel.MaxParticipants"/>, §4.2).</summary>
    public const int DayGuestRows = 100;

    /// <summary>What marks an allergy in the file — the one value its dropdown offers.</summary>
    public const string Yes = "Ja";

    public const string NotesHeader = "Allergianteckning (om \"Annat\" eller övrigt)";

    public const string FileName = "Deltagarmall.xlsx";

    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Column positions (1-based). Day guests carry one extra column, the day, after the age.
    public const int NumberColumn = 1;
    public const int NameColumn = 2;
    public const int AgeColumn = 3;
    public const int DayColumn = 4;

    public static int FirstAllergyColumn(bool dayGuest) => dayGuest ? 5 : 4;

    public static int NotesColumn(bool dayGuest) =>
        FirstAllergyColumn(dayGuest) + BookingDisplay.SelectableAllergies.Count;

    /// <summary>The header row, exactly as the template writes it and the upload expects it.</summary>
    public static IReadOnlyList<string> Headers(bool dayGuest)
    {
        var headers = new List<string> { "Nr", "Namn", "Ålderskategori" };
        if (dayGuest) headers.Add("Dag");
        headers.AddRange(BookingDisplay.SelectableAllergies.Select(BookingDisplay.AllergyLabel));
        headers.Add(NotesHeader);
        return headers;
    }

    /// <summary>The age dropdown — the same labels, in the same order, as the form's age select
    /// (both list the enum through <see cref="BookingDisplay.AgeLabel"/>).</summary>
    public static IReadOnlyList<string> AgeOptions() =>
        Enum.GetValues<AgeCategory>().Select(BookingDisplay.AgeLabel).ToList();

    /// <summary>The day dropdown — the stay's days, named as the form names them.</summary>
    public static IReadOnlyList<string> DayOptions(IReadOnlyList<DateOnly> stayDates) =>
        stayDates.Select(BookingDisplay.DayLabel).ToList();

    private static readonly XLColor HeaderFill = XLColor.FromHtml("#3F6A0F");   // --color-primary-ink
    private static readonly XLColor TitleColour = XLColor.FromHtml("#3F6A0F");
    private static readonly XLColor MissingFill = XLColor.FromHtml("#F6DCD6");  // --color-err tint
    private static readonly XLColor GridLine = XLColor.FromHtml("#BBBBBB");

    public static byte[] Build(ParticipantTemplateShape shape)
    {
        using var workbook = new XLWorkbook();

        var overnight = workbook.Worksheets.Add(OvernightSheet);
        var dayGuests = shape.IncludesDayGuests ? workbook.Worksheets.Add(DayGuestSheet) : null;
        var lists = WriteLists(workbook, shape);

        var guests = shape.OvernightCount == 1 ? "1 övernattande gäst" : $"{shape.OvernightCount} övernattande gäster";
        WriteSheet(overnight, OvernightSheet,
            $"Bokningen gäller {guests} – fyll i en rad per person. Namn och ålderskategori krävs; "
            + $"röda fält saknas fortfarande. Markera allergier med \"{Yes}\". Ladda sedan upp filen i "
            + "formuläret och kontrollera uppgifterna innan du skickar in. Uppladdningen ersätter alla "
            + "deltagare och dagsgäster som redan står i formuläret.",
            dayGuest: false, rows: shape.OvernightCount, lists);

        if (dayGuests is not null)
        {
            WriteSheet(dayGuests, DayGuestSheet,
                "Deltar endast på måltider (ingen övernattning). En rad per person och dag – kommer någon "
                + "två dagar, fyll i två rader. Lämna bladet tomt om ni inte har dagsgäster. Röda fält "
                + "saknas på en påbörjad rad. Fler än " + DayGuestRows + " dagsgäster? Ladda upp filen "
                + "först och lägg sedan till resten direkt i formuläret.",
                dayGuest: true, rows: DayGuestRows, lists);
        }

        overnight.SetTabActive();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>The hidden sheet with the dropdown values: ages in A, days in B, "Ja" in C.</summary>
    private static ListRanges WriteLists(XLWorkbook workbook, ParticipantTemplateShape shape)
    {
        var ws = workbook.Worksheets.Add(ListsSheet);

        var ages = AgeOptions();
        for (var i = 0; i < ages.Count; i++)
            ws.Cell(i + 1, 1).Value = ages[i];

        var days = DayOptions(shape.StayDates);
        for (var i = 0; i < days.Count; i++)
            ws.Cell(i + 1, 2).Value = days[i];

        ws.Cell(1, 3).Value = Yes;

        ws.Visibility = XLWorksheetVisibility.Hidden;
        ws.Protect();

        return new ListRanges(
            ws.Range(1, 1, ages.Count, 1),
            days.Count == 0 ? null : ws.Range(1, 2, days.Count, 2),
            ws.Range(1, 3, 1, 3));
    }

    private sealed record ListRanges(IXLRange Ages, IXLRange? Days, IXLRange Yes);

    private static void WriteSheet(
        IXLWorksheet ws, string title, string intro, bool dayGuest, int rows, ListRanges lists)
    {
        var headers = Headers(dayGuest);
        var lastColumn = headers.Count;

        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(1, 1).Style.Font.FontColor = TitleColour;

        var introRange = ws.Range(2, 1, 2, lastColumn).Merge();
        introRange.FirstCell().Value = intro;
        introRange.Style.Alignment.WrapText = true;
        introRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        ws.Row(2).Height = 62;

        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(HeaderRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }
        ws.Row(HeaderRow).Height = 32;
        ws.SheetView.FreezeRows(HeaderRow);

        ws.Column(NumberColumn).Width = 5;
        ws.Column(NameColumn).Width = 30;
        ws.Column(AgeColumn).Width = 16;
        if (dayGuest) ws.Column(DayColumn).Width = 20;
        for (var c = FirstAllergyColumn(dayGuest); c < NotesColumn(dayGuest); c++)
            ws.Column(c).Width = 18;
        ws.Column(NotesColumn(dayGuest)).Width = 45;

        if (rows == 0)
        {
            ws.Protect();
            return;
        }

        var first = HeaderRow + 1;
        var last = HeaderRow + rows;

        for (var r = first; r <= last; r++)
            ws.Cell(r, NumberColumn).Value = r - HeaderRow;
        ws.Range(first, NumberColumn, last, NumberColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var table = ws.Range(HeaderRow, 1, last, lastColumn).Style.Border;
        table.InsideBorder = XLBorderStyleValues.Thin;
        table.OutsideBorder = XLBorderStyleValues.Thin;
        table.InsideBorderColor = GridLine;
        table.OutsideBorderColor = GridLine;

        // Only the input cells are editable; the number column, header and instructions stay locked.
        ws.Range(first, NameColumn, last, lastColumn).Style.Protection.SetLocked(false);

        // Name: text only, within the form's limit.
        var name = ws.Range(first, NameColumn, last, NameColumn).CreateDataValidation();
        name.Custom($"AND(ISTEXT({Address(first, NameColumn)}),LEN({Address(first, NameColumn)})<={CompletionNormalizer.NameMaxLength})");
        Explain(name, "Namn", $"Skriv namnet som text, högst {CompletionNormalizer.NameMaxLength} tecken.");

        var age = ws.Range(first, AgeColumn, last, AgeColumn).CreateDataValidation();
        age.List(lists.Ages, true);
        Explain(age, "Ålderskategori", "Välj en ålderskategori i listan.");

        if (dayGuest && lists.Days is not null)
        {
            var day = ws.Range(first, DayColumn, last, DayColumn).CreateDataValidation();
            day.List(lists.Days, true);
            Explain(day, "Dag", "Välj en dag under vistelsen i listan.");
        }

        var allergies = ws.Range(first, FirstAllergyColumn(dayGuest), last, NotesColumn(dayGuest) - 1).CreateDataValidation();
        allergies.List(lists.Yes, true);
        Explain(allergies, "Allergi", $"Välj \"{Yes}\" eller lämna tomt.");

        var notes = ws.Range(first, NotesColumn(dayGuest), last, NotesColumn(dayGuest)).CreateDataValidation();
        notes.TextLength.EqualOrLessThan(CompletionNormalizer.NoteMaxLength);
        Explain(notes, "Allergianteckning", $"Högst {CompletionNormalizer.NoteMaxLength} tecken.");

        // Red while a required cell is empty. Every overnight row is required; a day-guest row only
        // once something on it has been filled in, so the 100 spare rows stay white.
        var required = ws.Range(first, NameColumn, last, dayGuest ? DayColumn : AgeColumn);
        var self = Address(first, NameColumn);
        var rowFilled = $"COUNTA($B{first}:${XLHelper.GetColumnLetterFromNumber(lastColumn)}{first})>0";
        required.AddConditionalFormat()
            .WhenIsTrue(dayGuest ? $"=AND(LEN(TRIM({self}))=0,{rowFilled})" : $"=LEN(TRIM({self}))=0")
            .Fill.SetBackgroundColor(MissingFill);

        ws.Protect();
    }

    private static void Explain(IXLDataValidation validation, string title, string message)
    {
        validation.IgnoreBlanks = true;
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = XLErrorStyle.Stop;
        validation.ErrorTitle = title;
        validation.ErrorMessage = message;
    }

    /// <summary>A relative A1 address — the top-left cell a range's formula is written against.</summary>
    private static string Address(int row, int column) =>
        $"{XLHelper.GetColumnLetterFromNumber(column)}{row}";
}
