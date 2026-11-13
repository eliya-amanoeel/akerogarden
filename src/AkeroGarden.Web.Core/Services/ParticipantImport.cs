using System.Globalization;
using System.IO.Compression;
using System.Xml;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using ClosedXML.Excel;

namespace AkeroGarden.Web.Core.Services;

/// <summary>The outcome of reading an uploaded participant file: either the full roster to put on
/// the form, or the reasons nothing was put there.</summary>
public sealed record ParticipantImportResult(
    IReadOnlyList<string> Errors,
    List<OvernightParticipantEntry> Overnight,
    List<DayGuestEntry> DayGuests)
{
    public bool Succeeded => Errors.Count == 0;

    public static ParticipantImportResult Failed(params string[] errors) => new(errors, [], []);
}

/// <summary>
/// Reads a filled-in participant template (FR-118) into completion-form rows.
///
/// <para><b>All or nothing.</b> Either every row is good and the whole roster is returned to replace
/// what is on the form, or nothing is returned and the guest is told what to fix, by the row number
/// printed in the file. A half-imported list is worse than none: the guest would have to work out
/// which people made it.</para>
///
/// <para><b>It fills the form; it never submits.</b> Whatever this returns is rendered into the form
/// and goes through <see cref="CompletionNormalizer"/> again when the guest submits, exactly as typed
/// input does — so the file's contents can never reach the booking by a path typing could not. The
/// per-row rules are the normalizer's own, applied here under the file's row numbers.</para>
///
/// <para><b>Reading an untrusted file.</b> The new exposure is the parse itself, so the file is
/// checked before ClosedXML ever opens it: size, that it is an Excel package at all, that it is not
/// macro-enabled, and that it does not unpack to more than <see cref="MaxUnpackedBytes"/> (a zip
/// bomb would otherwise take the single B1 instance, and the public site with it). Formulas are
/// never evaluated. The file is held in memory and dropped — never written to disk (the public host
/// buffers uploads up to <see cref="MaxFileBytes"/> in memory), stored, logged or forwarded —
/// and an error names a row and a problem, never a cell's contents, because the file holds allergy
/// data (health data, GDPR art. 9).</para>
/// </summary>
public static class ParticipantImport
{
    /// <summary>The largest file accepted. A filled template for 300 people is ~20–30 KB; anything
    /// near this is not our template. Checked by the page before the bytes are read, so a wrong
    /// pick (a photo) gets a message instead of an error page.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>The most the package may unpack to, counted as it is actually decompressed — a zip
    /// entry's declared size can lie.</summary>
    public const long MaxUnpackedBytes = 10 * 1024 * 1024;

    /// <summary>The most parts a package may hold. Our template has about a dozen.</summary>
    public const int MaxPackageEntries = 100;

    /// <summary>The most rows read across both sheets — the same bound as the form itself
    /// (<see cref="CompletionFormModel.MaxParticipants"/>), so a file can never fill in more than the
    /// form can post.</summary>
    public const int MaxRows = CompletionFormModel.MaxParticipants;

    /// <summary>How many row errors are listed before the rest are summarised as a count.</summary>
    public const int MaxErrorsShown = 20;

    public const string NoFile = "Välj filen du har fyllt i innan du laddar upp den.";
    public const string TooLarge = "Filen är för stor för att vara vår mall. Kontrollera att du har valt rätt fil.";
    public const string NotExcel = "Filen är inte en Excel-fil (.xlsx). Ladda ner mallen och fyll i den.";
    public const string OldExcel = "Filen är sparad i det gamla Excel-formatet. Spara den som Excel-arbetsbok (.xlsx) och ladda upp den igen.";
    public const string MacroEnabled = "Filen innehåller makron och tas inte emot. Spara den som en vanlig Excel-arbetsbok (.xlsx) och ladda upp den igen.";
    public const string Unreadable = "Filen kunde inte läsas. Ladda ner mallen på nytt och fyll i den.";
    public const string NotOurTemplate = "Filen är inte vår mall. Ladda ner mallen och fyll i den.";

    private const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string MacroWorkbookContentType = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";

    /// <summary>DTDs prohibited and no resolver: an entity declaration is refused, never expanded
    /// or fetched.</summary>
    private static readonly XmlReaderSettings SafeXml = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    // The legacy binary .xls format (an OLE compound file) starts with these bytes.
    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public static ParticipantImportResult Read(byte[] file, ParticipantTemplateShape shape)
    {
        if (file.Length == 0)
            return ParticipantImportResult.Failed(NoFile);
        if (file.Length > MaxFileBytes)
            return ParticipantImportResult.Failed(TooLarge);
        if (file.AsSpan().StartsWith(OleSignature))
            return ParticipantImportResult.Failed(OldExcel);

        var packageError = CheckPackage(file);
        if (packageError is not null)
            return ParticipantImportResult.Failed(packageError);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(new MemoryStream(file, writable: false));
        }
        catch (Exception)
        {
            // Whatever the parser objected to, the guest's remedy is the same. The exception is not
            // logged: its message can quote the file.
            return ParticipantImportResult.Failed(Unreadable);
        }

        using (workbook)
        {
            try
            {
                return ReadWorkbook(workbook, shape);
            }
            catch (Exception)
            {
                return ParticipantImportResult.Failed(Unreadable);
            }
        }
    }

    /// <summary>
    /// Checks the package before ClosedXML sees it: that it is a zip at all, that it declares an
    /// ordinary (not macro-enabled) workbook, and that it does not unpack beyond the bound. Returns
    /// the guest-facing error, or null when it may be opened.
    /// </summary>
    private static string? CheckPackage(byte[] file)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(file, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxPackageEntries)
                return NotOurTemplate;

            // Decompress every entry and count what actually comes out, rather than trusting the
            // sizes the archive declares.
            long unpacked = 0;
            var buffer = new byte[81920];
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    unpacked += read;
                    if (unpacked > MaxUnpackedBytes)
                        return TooLarge;
                }
            }

            var types = zip.GetEntry("[Content_Types].xml");
            if (types is null)
                return NotExcel;

            var declared = ReadContentTypes(types);
            if (declared.Contains(MacroWorkbookContentType))
                return MacroEnabled;
            if (!declared.Contains(WorkbookContentType))
                return NotExcel;

            // Every XML part is read once under our own settings before ClosedXML parses it, so a
            // DTD — the carrier of entity expansion and external entities (XXE) — is refused here,
            // whatever the library underneath would have done with it.
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = entry.Open();
                    using var reader = XmlReader.Create(stream, SafeXml);
                    while (reader.Read())
                    {
                    }
                }
            }

            return null;
        }
        catch (InvalidDataException)
        {
            return NotExcel;     // not a zip at all — a renamed PDF, a CSV, an image
        }
        catch (XmlException)
        {
            return Unreadable;   // includes a DTD, which is prohibited below
        }
    }

    /// <summary>Every content type the package declares. Parsed with DTDs prohibited and no
    /// resolver, so an entity declaration is refused rather than expanded or fetched.</summary>
    private static HashSet<string> ReadContentTypes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, SafeXml);

        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.GetAttribute("ContentType") is { } type)
                types.Add(type);
        }
        return types;
    }

    private static ParticipantImportResult ReadWorkbook(XLWorkbook workbook, ParticipantTemplateShape shape)
    {
        if (!workbook.Worksheets.TryGetWorksheet(ParticipantTemplate.OvernightSheet, out var overnightSheet)
            || !HasHeaders(overnightSheet, dayGuest: false))
            return ParticipantImportResult.Failed(NotOurTemplate);

        IXLWorksheet? dayGuestSheet = null;
        if (shape.IncludesDayGuests
            && (!workbook.Worksheets.TryGetWorksheet(ParticipantTemplate.DayGuestSheet, out dayGuestSheet)
                || !HasHeaders(dayGuestSheet, dayGuest: true)))
            return ParticipantImportResult.Failed(NotOurTemplate);

        var overnightLast = LastDataRow(overnightSheet, dayGuest: false);
        var dayGuestLast = dayGuestSheet is null ? ParticipantTemplate.HeaderRow : LastDataRow(dayGuestSheet, dayGuest: true);
        if (overnightLast - ParticipantTemplate.HeaderRow + dayGuestLast - ParticipantTemplate.HeaderRow > MaxRows)
            return ParticipantImportResult.Failed($"Filen har fler än {MaxRows} rader och kan inte läsas in.");

        var errors = new List<string>();

        if (overnightLast > ParticipantTemplate.HeaderRow + shape.OvernightCount)
        {
            errors.Add($"Bladet \"{ParticipantTemplate.OvernightSheet}\" har fler rader än bokningens "
                + $"{shape.OvernightCount} övernattande gäster. Ta bort de extra raderna.");
        }

        var overnight = new List<OvernightParticipantEntry>(shape.OvernightCount);
        for (var n = 1; n <= shape.OvernightCount; n++)
        {
            var row = overnightSheet.Row(ParticipantTemplate.HeaderRow + n);
            var label = $"Deltagare {n}";
            var rowErrors = new List<string>();

            var entry = new OvernightParticipantEntry
            {
                FullName = ReadName(row, label, rowErrors),
                AgeCategory = ReadAge(row, label, rowErrors) ?? AgeCategory.Adult,
                Allergies = ReadAllergies(row, dayGuest: false, label, rowErrors),
                AllergyNotes = ReadNote(row, dayGuest: false, label, rowErrors),
            };

            // The normalizer's remaining rules (length, "Annat" needs a note) only once the row reads
            // cleanly — otherwise a missing name would be reported twice.
            errors.AddRange(rowErrors.Count > 0 ? rowErrors : CompletionNormalizer.ValidateOvernightEntry(entry, label));
            overnight.Add(entry);
        }

        var dayGuests = new List<DayGuestEntry>();
        if (dayGuestSheet is not null && shape.StayDates.Count > 0)
        {
            var checkIn = shape.StayDates[0];
            var checkOut = shape.StayDates[^1];

            for (var r = ParticipantTemplate.HeaderRow + 1; r <= dayGuestLast; r++)
            {
                var row = dayGuestSheet.Row(r);
                if (IsEmptyRow(row, dayGuest: true))
                    continue;

                var label = $"Dagsgäst {r - ParticipantTemplate.HeaderRow}";
                var rowErrors = new List<string>();

                var entry = new DayGuestEntry
                {
                    FullName = ReadName(row, label, rowErrors),
                    AgeCategory = ReadAge(row, label, rowErrors) ?? AgeCategory.Adult,
                    AttendanceDate = ReadDay(row, shape.StayDates, label, rowErrors),
                    Allergies = ReadAllergies(row, dayGuest: true, label, rowErrors),
                    AllergyNotes = ReadNote(row, dayGuest: true, label, rowErrors),
                };

                // A started row must be complete — name, age and day are each asked for above, so a row
                // with only an allergy ticked is reported rather than quietly skipped, as the
                // normalizer would skip a row with no name and no day.
                errors.AddRange(rowErrors.Count > 0
                    ? rowErrors
                    : CompletionNormalizer.ValidateDayGuestEntry(entry, label, checkIn, checkOut));
                dayGuests.Add(entry);
            }
        }

        if (errors.Count > 0)
            return new ParticipantImportResult(Summarise(errors), [], []);

        return new ParticipantImportResult([], overnight, dayGuests);
    }

    private static List<string> Summarise(List<string> errors)
    {
        if (errors.Count <= MaxErrorsShown)
            return errors;

        var shown = errors.Take(MaxErrorsShown).ToList();
        shown.Add($"… och {errors.Count - MaxErrorsShown} fel till.");
        return shown;
    }

    private static bool HasHeaders(IXLWorksheet sheet, bool dayGuest)
    {
        var expected = ParticipantTemplate.Headers(dayGuest);
        for (var c = 0; c < expected.Count; c++)
        {
            var cell = sheet.Cell(ParticipantTemplate.HeaderRow, c + 1);
            if (cell.HasFormula || !cell.Value.IsText
                || !string.Equals(cell.Value.GetText().Trim(), expected[c], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>The last row below the header with anything in an input column. The number column
    /// is ignored: the template pre-fills it on rows nobody uses.</summary>
    private static int LastDataRow(IXLWorksheet sheet, bool dayGuest)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? ParticipantTemplate.HeaderRow;
        while (last > ParticipantTemplate.HeaderRow && IsEmptyRow(sheet.Row(last), dayGuest))
            last--;
        return last;
    }

    private static bool IsEmptyRow(IXLRow row, bool dayGuest)
    {
        for (var c = ParticipantTemplate.NameColumn; c <= ParticipantTemplate.NotesColumn(dayGuest); c++)
        {
            var cell = row.Cell(c);
            if (cell.HasFormula)
                return false;
            var value = cell.Value;
            if (!value.IsBlank && !(value.IsText && string.IsNullOrWhiteSpace(value.GetText())))
                return false;
        }
        return true;
    }

    /// <summary>A cell's content as text. A formula is refused, never evaluated — reading
    /// <see cref="IXLCell.Value"/> on a formula cell would run it.</summary>
    private static bool TryReadText(IXLCell cell, string label, string field, List<string> errors, out string? text)
    {
        text = null;
        if (cell.HasFormula)
        {
            errors.Add($"{label}: {field} innehåller en formel. Skriv in värdet i stället.");
            return false;
        }

        var value = cell.Value;
        if (value.IsBlank)
            return true;
        if (value.IsText)
        {
            text = string.IsNullOrWhiteSpace(value.GetText()) ? null : value.GetText().Trim();
            return true;
        }
        if (value.IsNumber)
        {
            text = value.GetNumber().ToString(CultureInfo.GetCultureInfo("sv-SE"));
            return true;
        }

        errors.Add($"{label}: {field} har ett värde som inte kan läsas.");
        return false;
    }

    /// <summary>The name, required. Asked for here rather than left to the normalizer so that an
    /// empty row reports its missing name and its missing age together.</summary>
    private static string? ReadName(IXLRow row, string label, List<string> errors)
    {
        var cell = row.Cell(ParticipantTemplate.NameColumn);
        if (!cell.HasFormula && cell.Value.IsNumber)
        {
            errors.Add($"{label}: namnet ska vara text.");
            return null;
        }
        if (!TryReadText(cell, label, "namnet", errors, out var text))
            return null;
        if (text is null)
            errors.Add($"{label}: namn krävs.");
        return text;
    }

    private static AgeCategory? ReadAge(IXLRow row, string label, List<string> errors)
    {
        if (!TryReadText(row.Cell(ParticipantTemplate.AgeColumn), label, "ålderskategorin", errors, out var text))
            return null;
        if (text is null)
        {
            errors.Add($"{label}: välj ålderskategori.");
            return null;
        }

        foreach (var age in Enum.GetValues<AgeCategory>())
        {
            if (string.Equals(text, BookingDisplay.AgeLabel(age), StringComparison.OrdinalIgnoreCase))
                return age;
        }

        errors.Add($"{label}: okänd ålderskategori – välj i listan.");
        return null;
    }

    /// <summary>The day, as the form's <c>yyyy-MM-dd</c> value. Only the dropdown's own words are
    /// accepted, exactly as for the age: the list is the stay's days, so a day it offers is always
    /// within the stay. A date pasted into the cell is refused like any other unknown value — the
    /// dropdown is the way in, and one rule is simpler than reading Excel's date cells too.</summary>
    private static string? ReadDay(IXLRow row, IReadOnlyList<DateOnly> stayDates, string label, List<string> errors)
    {
        var cell = row.Cell(ParticipantTemplate.DayColumn);
        if (!cell.HasFormula && cell.Value.IsDateTime)
        {
            errors.Add($"{label}: okänd dag – välj en dag under vistelsen i listan.");
            return null;
        }

        if (!TryReadText(cell, label, "dagen", errors, out var text))
            return null;
        if (text is null)
        {
            errors.Add($"{label}: välj vilken dag gästen deltar.");
            return null;
        }

        foreach (var date in stayDates)
        {
            if (string.Equals(text, BookingDisplay.DayLabel(date), StringComparison.OrdinalIgnoreCase))
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        errors.Add($"{label}: okänd dag – välj en dag under vistelsen i listan.");
        return null;
    }

    private static List<Allergy> ReadAllergies(IXLRow row, bool dayGuest, string label, List<string> errors)
    {
        var allergies = new List<Allergy>();
        var first = ParticipantTemplate.FirstAllergyColumn(dayGuest);

        for (var i = 0; i < BookingDisplay.SelectableAllergies.Count; i++)
        {
            var allergy = BookingDisplay.SelectableAllergies[i];
            var field = BookingDisplay.AllergyLabel(allergy).ToLower(CultureInfo.GetCultureInfo("sv-SE"));
            if (!TryReadText(row.Cell(first + i), label, field, errors, out var text) || text is null)
                continue;

            // "Ja" is what the dropdown offers; an "x" is what people type when they tick a box.
            if (text.Equals(ParticipantTemplate.Yes, StringComparison.OrdinalIgnoreCase)
                || text.Equals("x", StringComparison.OrdinalIgnoreCase))
                allergies.Add(allergy);
            else if (!text.Equals("nej", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{label}: markera {field} med \"{ParticipantTemplate.Yes}\" eller lämna tomt.");
        }

        return allergies;
    }

    private static string? ReadNote(IXLRow row, bool dayGuest, string label, List<string> errors) =>
        TryReadText(row.Cell(ParticipantTemplate.NotesColumn(dayGuest)), label, "allergianteckningen", errors, out var text)
            ? text
            : null;
}
