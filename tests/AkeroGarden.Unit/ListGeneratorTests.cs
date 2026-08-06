using System.Diagnostics;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using ClosedXML.Excel;

namespace AkeroGarden.Unit;

/// <summary>
/// Coverage for the PDF (QuestPDF) and Excel (ClosedXML) list generators (FR-44/45): they produce
/// valid, non-empty documents, and meet the NFR-10 performance target (≤10 s for 300 participants).
/// </summary>
public class ListGeneratorTests
{
    private static BookingListData Data(int participantCount)
    {
        var booking = new Booking
        {
            BookingReference = "AG-2026-0042",
            ContactName = "Förening X",
            CheckIn = new DateOnly(2026, 8, 1),
            CheckOut = new DateOnly(2026, 8, 4),
        };
        for (var i = 0; i < participantCount; i++)
        {
            var p = new Participant
            {
                FullName = $"Deltagare {i:000}",
                ParticipantType = i % 5 == 0 ? ParticipantType.DayGuest : ParticipantType.Overnight,
                AgeCategory = AgeCategory.Adult,
                IsFullBoard = true,
                AttendsBreakfast = true,
                AttendanceDate = i % 5 == 0 ? new DateOnly(2026, 8, 2) : null,
                DietaryRequirements = i % 3 == 0 ? [DietaryRequirement.Vegetarian] : [DietaryRequirement.None],
            };
            p.MealPreferences.Add(new MealPreference { Date = new DateOnly(2026, 8, 1), MealType = MealType.Dinner, MealOptionSnapshot = "Köttbullar" });
            p.MealPreferences.Add(new MealPreference { Date = new DateOnly(2026, 8, 2), MealType = MealType.Lunch, MealOptionSnapshot = "Fisk" });
            booking.Participants.Add(p);
        }
        return ListShaper.Shape(booking);
    }

    [Fact]
    public void Pdf_IsWellFormed_AndScalesWithContent()
    {
        // QuestPDF output isn't cheaply text-extractable, so the PDF path asserts structural validity
        // (magic bytes) PLUS that the document grows materially with row count — a real signal that
        // participants are rendered into it, not a fixed empty shell. Content fidelity (that the
        // shaped values themselves are correct) is asserted exhaustively in ListShaperTests, and the
        // shaped-value → cell mapping is asserted on the Excel path below.
        var small = ListPdfGenerator.ParticipantList(Data(3));
        var large = ListPdfGenerator.ParticipantList(Data(60));

        // PDF magic bytes "%PDF".
        Assert.Equal([0x25, 0x50, 0x44, 0x46], small.Take(4).ToArray());
        Assert.Equal([0x25, 0x50, 0x44, 0x46], large.Take(4).ToArray());
        // QuestPDF embeds fonts, so a small list already carries a large fixed baseline and content
        // grows sub-linearly against the total byte count — assert a material increase (57 extra
        // participants add several KB of rows), not a multiple, so this proves rows render without a
        // brittle ratio.
        Assert.True(large.Length > small.Length + 2000,
            $"A 60-participant list ({large.Length} B) should be materially larger than a 3-participant " +
            $"one ({small.Length} B) — if it isn't, participant rows aren't being rendered into the PDF.");

        var food = ListPdfGenerator.FoodPreferenceList(Data(3));
        Assert.Equal([0x25, 0x50, 0x44, 0x46], food.Take(4).ToArray());
    }

    [Fact]
    public void Excel_ParticipantList_ContainsTheShapedRows()
    {
        var xlsx = ListExcelGenerator.ParticipantList(Data(3));
        // XLSX is a ZIP → "PK" magic bytes.
        Assert.Equal([0x50, 0x4B], xlsx.Take(2).ToArray());

        // Re-open the workbook and prove the shaped data actually reached the cells — not just that
        // a well-formed (but possibly empty) file was produced.
        var cells = SheetStrings(xlsx, "Deltagarlista");
        Assert.Contains("Namn", cells);                 // header rendered
        Assert.Contains("Deltagare 000", cells);        // first participant
        Assert.Contains("Deltagare 001", cells);
        Assert.Contains("Deltagare 002", cells);
        Assert.Contains("Vegetarian", cells);           // Deltagare 000's dietary requirement (i % 3 == 0)
    }

    [Fact]
    public void Excel_ParticipantList_KeepsNarvaroAndKostbehov_UnlikeThePdf()
    {
        // Decision 2026-08-06: the printed roster is trimmed to Namn/Typ/Ålder, but the Excel stays
        // the fuller working copy. Asserting the divergence deliberately, so a later "tidy-up" that
        // silently drops these from the spreadsheet too fails here.
        var cells = SheetStrings(ListExcelGenerator.ParticipantList(Data(5)), "Deltagarlista");

        Assert.Contains("Närvaro", cells);
        Assert.Contains("Kostbehov", cells);
    }

    [Fact]
    public void Excel_BothLists_StateTheBookingReference()
    {
        // FR-92: the reference identifies the booking on every artefact. It was missing from all
        // four downloads until 2026-08-06.
        var data = Data(3);

        Assert.Contains("AG-2026-0042", SheetStrings(ListExcelGenerator.ParticipantList(data), "Deltagarlista").First());
        Assert.Contains("AG-2026-0042", SheetStrings(ListExcelGenerator.FoodPreferenceList(data), "Tillagning").First());

        // COVERAGE LIMIT, stated rather than papered over: the PDFs are binary and QuestPDF offers
        // no text extraction, so this test does NOT verify the reference is rendered on the PDF —
        // only that the generators receive it. The PDF heading is verified by eye on staging. Adding
        // a PDF text-extraction dependency for one assertion was judged not worth it; revisit if the
        // PDF headers grow more logic.
        Assert.Equal("AG-2026-0042", data.BookingReference);
    }

    [Fact]
    public void Excel_FoodList_ContainsPrepCountsAndSpecialDiets()
    {
        var xlsx = ListExcelGenerator.FoodPreferenceList(Data(3));
        Assert.Equal([0x50, 0x4B], xlsx.Take(2).ToArray());

        // Sheet 1 (Tillagning): the seeded dishes are aggregated into the prep summary.
        var prep = SheetStrings(xlsx, "Tillagning");
        Assert.Contains("Köttbullar", prep);
        Assert.Contains("Fisk", prep);

        // Sheet 2 (Kostbehov): only people with a dietary need, named, with the requirement.
        var diets = SheetStrings(xlsx, "Kostbehov");
        Assert.Contains("Deltagare 000", diets);        // i % 3 == 0 → Vegetarian
        Assert.Contains("Vegetarian", diets);
    }

    /// <summary>Re-opens a produced workbook and returns every used cell's string value on the named
    /// sheet, so a test can assert the shaped data survived into the actual file.</summary>
    private static List<string> SheetStrings(byte[] xlsx, string sheet)
    {
        using var stream = new MemoryStream(xlsx);
        using var workbook = new XLWorkbook(stream);
        return workbook.Worksheet(sheet).CellsUsed().Select(c => c.GetString()).ToList();
    }

    [Fact]
    public void AllFourLists_For300Participants_GenerateWithinTenSeconds()
    {
        var data = Data(300);
        var sw = Stopwatch.StartNew();

        var a = ListPdfGenerator.ParticipantList(data);
        var b = ListPdfGenerator.FoodPreferenceList(data);
        var c = ListExcelGenerator.ParticipantList(data);
        var d = ListExcelGenerator.FoodPreferenceList(data);

        sw.Stop();

        Assert.All([a, b, c, d], bytes => Assert.True(bytes.Length > 0));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"NFR-10: 300-participant lists took {sw.Elapsed.TotalSeconds:F1}s (target < 10s).");
    }
}
