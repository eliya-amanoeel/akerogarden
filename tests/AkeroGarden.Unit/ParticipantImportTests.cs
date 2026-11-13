using System.IO.Compression;
using System.Text;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using ClosedXML.Excel;

namespace AkeroGarden.Unit;

/// <summary>
/// Reading an uploaded participant file (FR-118). Two halves, both asserted: the roster comes back
/// whole or not at all, with the file's own row numbers in every message; and a file that is not
/// our template — the wrong type, too large, macro-enabled, a zip bomb, a DTD — is refused before
/// ClosedXML parses it. Each file-handling case in the plan has its own crafted file here.
/// </summary>
public class ParticipantImportTests
{
    private static readonly DateOnly CheckIn = new(2027, 6, 11);

    private static ParticipantTemplateShape Shape(int overnight = 3, bool dayGuests = true) =>
        new(overnight, [CheckIn, CheckIn.AddDays(1), CheckIn.AddDays(2)], dayGuests);

    private static int Row(int number) => ParticipantTemplate.HeaderRow + number;

    /// <summary>The booking's template, filled in by <paramref name="fill"/> the way a guest would.</summary>
    private static byte[] Filled(ParticipantTemplateShape shape, Action<IXLWorksheet, IXLWorksheet?> fill)
    {
        using var workbook = new XLWorkbook(new MemoryStream(ParticipantTemplate.Build(shape)));
        var overnight = workbook.Worksheet(ParticipantTemplate.OvernightSheet);
        workbook.Worksheets.TryGetWorksheet(ParticipantTemplate.DayGuestSheet, out var dayGuests);
        fill(overnight, dayGuests);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Every overnight row complete — name and age — so a test can break just one thing.</summary>
    private static void Complete(IXLWorksheet sheet, int count)
    {
        for (var n = 1; n <= count; n++)
        {
            sheet.Cell(Row(n), ParticipantTemplate.NameColumn).Value = $"Person {n}";
            sheet.Cell(Row(n), ParticipantTemplate.AgeColumn).Value = "Vuxen (4+)";
        }
    }

    private static int AllergyColumn(Allergy allergy, bool dayGuest) =>
        ParticipantTemplate.FirstAllergyColumn(dayGuest) + BookingDisplay.SelectableAllergies.ToList().IndexOf(allergy);

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(content);
            }
        }
        return stream.ToArray();
    }

    /// <summary>A real template with one of its parts rewritten — for the macro and DTD cases, which
    /// must be otherwise-valid workbooks to prove the check, not a parse error, refused them.</summary>
    private static byte[] Rewritten(string part, Func<string, string> rewrite)
    {
        var stream = new MemoryStream();
        stream.Write(ParticipantTemplate.Build(Shape()));
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(part)!;
            string text;
            using (var reader = new StreamReader(entry.Open()))
                text = reader.ReadToEnd();
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(part).Open());
            writer.Write(rewrite(text));
        }
        return stream.ToArray();
    }

    private static string Only(ParticipantImportResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Empty(result.Overnight);
        Assert.Empty(result.DayGuests);
        return Assert.Single(result.Errors);
    }

    // ── A filled template ──────────────────────────────────────────────────────

    [Fact]
    public void A_filled_template_returns_every_overnight_row_and_the_started_day_guest_rows()
    {
        var file = Filled(Shape(), (overnight, dayGuests) =>
        {
            Complete(overnight, 3);
            overnight.Cell(Row(2), ParticipantTemplate.AgeColumn).Value = "Barn (0–3)";
            overnight.Cell(Row(2), AllergyColumn(Allergy.Nut, false)).Value = "Ja";
            overnight.Cell(Row(3), AllergyColumn(Allergy.Other, false)).Value = "Ja";
            overnight.Cell(Row(3), ParticipantTemplate.NotesColumn(false)).Value = "Sesam";

            dayGuests!.Cell(Row(1), ParticipantTemplate.NameColumn).Value = "Dag Dagsson";
            dayGuests.Cell(Row(1), ParticipantTemplate.AgeColumn).Value = "Vuxen (4+)";
            dayGuests.Cell(Row(1), ParticipantTemplate.DayColumn).Value = "Lördag 12 juni";
            // Row 2–6 left empty on purpose: blank rows between entries are skipped, not errors.
            dayGuests.Cell(Row(7), ParticipantTemplate.NameColumn).Value = "Eva Ek";
            dayGuests.Cell(Row(7), ParticipantTemplate.AgeColumn).Value = "Barn (0–3)";
            dayGuests.Cell(Row(7), ParticipantTemplate.DayColumn).Value = "Söndag 13 juni";
            dayGuests.Cell(Row(7), AllergyColumn(Allergy.Gluten, true)).Value = "Ja";
        });

        var result = ParticipantImport.Read(file, Shape());

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(["Person 1", "Person 2", "Person 3"], result.Overnight.Select(p => p.FullName));
        Assert.Equal(AgeCategory.Child, result.Overnight[1].AgeCategory);
        Assert.Equal([Allergy.Nut], result.Overnight[1].Allergies);
        Assert.Equal([Allergy.Other], result.Overnight[2].Allergies);
        Assert.Equal("Sesam", result.Overnight[2].AllergyNotes);

        Assert.Equal(2, result.DayGuests.Count);
        Assert.Equal("2027-06-12", result.DayGuests[0].AttendanceDate);
        Assert.Equal("2027-06-13", result.DayGuests[1].AttendanceDate);
        Assert.Equal(AgeCategory.Child, result.DayGuests[1].AgeCategory);
        Assert.Equal([Allergy.Gluten], result.DayGuests[1].Allergies);
    }

    [Fact]
    public void An_empty_day_guest_sheet_means_no_day_guests()
    {
        var result = ParticipantImport.Read(Filled(Shape(), (o, _) => Complete(o, 3)), Shape());

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Empty(result.DayGuests);
    }

    [Fact]
    public void A_self_catering_template_has_no_day_guest_sheet_to_read()
    {
        var shape = Shape(dayGuests: false);
        var result = ParticipantImport.Read(Filled(shape, (o, d) =>
        {
            Assert.Null(d);
            Complete(o, 3);
        }), shape);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(3, result.Overnight.Count);
    }

    [Fact]
    public void An_allergy_may_be_marked_x_and_nej_means_none()
    {
        var file = Filled(Shape(), (o, _) =>
        {
            Complete(o, 3);
            o.Cell(Row(1), AllergyColumn(Allergy.Lactose, false)).Value = "x";
            o.Cell(Row(2), AllergyColumn(Allergy.Lactose, false)).Value = "Nej";
        });

        var result = ParticipantImport.Read(file, Shape());

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal([Allergy.Lactose], result.Overnight[0].Allergies);
        Assert.Empty(result.Overnight[1].Allergies);
    }

    // ── Row rules: all or nothing, by the file's own row numbers ───────────────

    [Fact]
    public void One_bad_row_imports_nothing_and_names_that_row()
    {
        var file = Filled(Shape(), (o, _) =>
        {
            Complete(o, 3);
            o.Cell(Row(2), ParticipantTemplate.NameColumn).Value = "";
        });

        Assert.Equal("Deltagare 2: namn krävs.", Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void An_empty_row_reports_its_missing_name_and_age_together()
    {
        var file = Filled(Shape(), (o, _) =>
        {
            Complete(o, 3);
            o.Cell(Row(3), ParticipantTemplate.NameColumn).Value = "";
            o.Cell(Row(3), ParticipantTemplate.AgeColumn).Value = "";
        });

        var result = ParticipantImport.Read(file, Shape());

        Assert.Empty(result.Overnight);
        Assert.Equal(["Deltagare 3: namn krävs.", "Deltagare 3: välj ålderskategori."], result.Errors);
    }

    [Fact]
    public void An_age_that_is_not_one_of_the_labels_is_refused()
    {
        var file = Filled(Shape(), (o, _) =>
        {
            Complete(o, 3);
            o.Cell(Row(1), ParticipantTemplate.AgeColumn).Value = "Vuxen";
        });

        Assert.Equal("Deltagare 1: okänd ålderskategori – välj i listan.", Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void A_formula_is_refused_and_never_evaluated()
    {
        var file = Filled(Shape(), (o, _) =>
        {
            Complete(o, 3);
            o.Cell(Row(1), ParticipantTemplate.NameColumn).FormulaA1 = "\"Anna\"&\" Andersson\"";
        });

        Assert.Equal(
            "Deltagare 1: namnet innehåller en formel. Skriv in värdet i stället.",
            Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void A_number_as_a_name_is_refused()
    {
        var file = Filled(Shape(), (o, _) =>
        {
            Complete(o, 3);
            o.Cell(Row(1), ParticipantTemplate.NameColumn).Value = 12345;
        });

        Assert.Equal("Deltagare 1: namnet ska vara text.", Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void An_allergy_marked_with_anything_else_is_refused()
    {
        var file = Filled(Shape(), (o, _) =>
        {
            Complete(o, 3);
            o.Cell(Row(1), AllergyColumn(Allergy.Nut, false)).Value = "kanske";
        });

        Assert.Equal("Deltagare 1: markera nötallergi med \"Ja\" eller lämna tomt.", Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void Annat_without_a_note_is_refused_by_the_forms_own_rule()
    {
        var file = Filled(Shape(), (o, _) =>
        {
            Complete(o, 3);
            o.Cell(Row(2), AllergyColumn(Allergy.Other, false)).Value = "Ja";
        });

        Assert.Equal("Deltagare 2: beskriv allergin i fältet för 'Annat'.", Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void Rows_beyond_the_bookings_count_are_refused()
    {
        var file = Filled(Shape(), (o, _) =>
        {
            Complete(o, 3);
            o.Cell(Row(4), ParticipantTemplate.NameColumn).Value = "En till";
        });

        Assert.Contains("fler rader än bokningens 3 övernattande gäster",
            Only(ParticipantImport.Read(file, Shape())), StringComparison.Ordinal);
    }

    [Fact]
    public void A_started_day_guest_row_must_be_complete_and_is_named_by_its_row_number()
    {
        var file = Filled(Shape(), (o, d) =>
        {
            Complete(o, 3);
            d!.Cell(Row(7), AllergyColumn(Allergy.Nut, true)).Value = "Ja";
        });

        var result = ParticipantImport.Read(file, Shape());

        Assert.Empty(result.Overnight);
        Assert.Equal(
            ["Dagsgäst 7: namn krävs.", "Dagsgäst 7: välj ålderskategori.", "Dagsgäst 7: välj vilken dag gästen deltar."],
            result.Errors);
    }

    [Fact]
    public void A_date_pasted_into_the_day_cell_is_refused_like_any_unknown_day()
    {
        // Only the dropdown's words are read (one rule, as for the age). A pasted date — even one
        // inside the stay — is refused rather than interpreted.
        var file = Filled(Shape(), (o, d) =>
        {
            Complete(o, 3);
            d!.Cell(Row(1), ParticipantTemplate.NameColumn).Value = "Dag Dagsson";
            d.Cell(Row(1), ParticipantTemplate.AgeColumn).Value = "Vuxen (4+)";
            d.Cell(Row(1), ParticipantTemplate.DayColumn).Value = new DateTime(2027, 6, 12);
        });

        Assert.Equal("Dagsgäst 1: okänd dag – välj en dag under vistelsen i listan.", Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void A_day_that_is_not_one_of_the_stay_days_is_refused()
    {
        var file = Filled(Shape(), (o, d) =>
        {
            Complete(o, 3);
            d!.Cell(Row(1), ParticipantTemplate.NameColumn).Value = "Dag Dagsson";
            d.Cell(Row(1), ParticipantTemplate.AgeColumn).Value = "Vuxen (4+)";
            d.Cell(Row(1), ParticipantTemplate.DayColumn).Value = "Måndag 14 juni";
        });

        Assert.Equal("Dagsgäst 1: okänd dag – välj en dag under vistelsen i listan.", Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void Many_errors_are_listed_up_to_the_limit_and_the_rest_counted()
    {
        var shape = Shape(overnight: 25);
        var file = Filled(shape, (o, _) =>
        {
            for (var n = 1; n <= 25; n++)
                o.Cell(Row(n), ParticipantTemplate.AgeColumn).Value = "Vuxen (4+)";
        });

        var result = ParticipantImport.Read(file, shape);

        Assert.Equal(ParticipantImport.MaxErrorsShown + 1, result.Errors.Count);
        Assert.Equal("… och 5 fel till.", result.Errors[^1]);
    }

    // ── Not our template ───────────────────────────────────────────────────────

    [Fact]
    public void Another_spreadsheet_is_not_our_template()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add("Blad1").Cell(1, 1).Value = "Namn";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        Assert.Equal(ParticipantImport.NotOurTemplate, Only(ParticipantImport.Read(stream.ToArray(), Shape())));
    }

    [Fact]
    public void Changed_headers_are_not_our_template()
    {
        var file = Filled(Shape(), (o, _) => o.Cell(ParticipantTemplate.HeaderRow, ParticipantTemplate.NameColumn).Value = "Förnamn");

        Assert.Equal(ParticipantImport.NotOurTemplate, Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void A_template_without_the_day_guest_sheet_is_not_this_bookings_template()
    {
        var file = Filled(Shape(dayGuests: false), (o, _) => Complete(o, 3));

        Assert.Equal(ParticipantImport.NotOurTemplate, Only(ParticipantImport.Read(file, Shape(dayGuests: true))));
    }

    // ── The file itself ────────────────────────────────────────────────────────

    [Fact]
    public void No_file_asks_for_one()
    {
        Assert.Equal(ParticipantImport.NoFile, Only(ParticipantImport.Read([], Shape())));
    }

    [Fact]
    public void A_file_over_the_size_limit_is_refused_unread()
    {
        var file = new byte[ParticipantImport.MaxFileBytes + 1];

        Assert.Equal(ParticipantImport.TooLarge, Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void A_renamed_pdf_is_not_an_excel_file()
    {
        var file = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n");

        Assert.Equal(ParticipantImport.NotExcel, Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void A_csv_is_not_an_excel_file()
    {
        var file = Encoding.UTF8.GetBytes("Namn;Ålderskategori\nAnna;Vuxen (4+)\n");

        Assert.Equal(ParticipantImport.NotExcel, Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void The_old_binary_xls_format_gets_its_own_message()
    {
        var file = new byte[512];
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(file, 0);

        Assert.Equal(ParticipantImport.OldExcel, Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void A_zip_that_is_not_a_workbook_is_not_an_excel_file()
    {
        // A Word document is a zip too; what it declares is what tells them apart.
        const string wordTypes =
            "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
            + "</Types>";
        var file = Zip(("[Content_Types].xml", Encoding.UTF8.GetBytes(wordTypes)));

        Assert.Equal(ParticipantImport.NotExcel, Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void A_macro_enabled_workbook_is_refused()
    {
        var file = Rewritten("[Content_Types].xml", types => types.Replace(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
            "application/vnd.ms-excel.sheet.macroEnabled.main+xml",
            StringComparison.Ordinal));

        Assert.Equal(ParticipantImport.MacroEnabled, Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void A_zip_bomb_is_refused_by_what_it_unpacks_to_not_what_it_declares()
    {
        // 11 MB of zeros compresses to a few kilobytes — well under the upload limit.
        var file = Zip(("xl/padding.bin", new byte[11 * 1024 * 1024]));
        Assert.True(file.Length < ParticipantImport.MaxFileBytes);

        Assert.Equal(ParticipantImport.TooLarge, Only(ParticipantImport.Read(file, Shape())));
    }

    [Fact]
    public void A_package_with_too_many_parts_is_refused()
    {
        var entries = Enumerable.Range(0, ParticipantImport.MaxPackageEntries + 1)
            .Select(i => ($"part{i}.bin", new byte[] { 1 }))
            .ToArray();

        Assert.Equal(ParticipantImport.NotOurTemplate, Only(ParticipantImport.Read(Zip(entries), Shape())));
    }

    [Fact]
    public void An_xml_part_carrying_a_dtd_is_refused_before_the_workbook_is_opened()
    {
        // The XXE carrier: an external entity in an otherwise valid workbook part. Refused by our own
        // DTD-prohibiting read, so the outcome does not depend on what ClosedXML would do with it.
        var file = Rewritten("xl/workbook.xml", xml =>
        {
            // After the XML declaration if there is one; the root element's prefix varies by writer.
            var declarationEnd = xml.IndexOf("?>", StringComparison.Ordinal);
            var start = declarationEnd < 0 ? 0 : declarationEnd + 2;
            return xml[..start]
                + "<!DOCTYPE workbook [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>"
                + xml[start..];
        });

        Assert.Equal(ParticipantImport.Unreadable, Only(ParticipantImport.Read(file, Shape())));
    }
}
