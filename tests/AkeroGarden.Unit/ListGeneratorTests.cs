using System.Diagnostics;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;

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
                RoomAssignmentSnapshot = i % 5 == 0 ? null : $"Rum {i % 20} — Blidö",
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
    public void Pdf_ProducesValidNonEmptyDocument()
    {
        var pdf = ListPdfGenerator.ParticipantList(Data(3));
        Assert.True(pdf.Length > 0);
        // PDF magic bytes "%PDF".
        Assert.Equal([0x25, 0x50, 0x44, 0x46], pdf.Take(4).ToArray());

        var food = ListPdfGenerator.FoodPreferenceList(Data(3));
        Assert.True(food.Length > 0);
    }

    [Fact]
    public void Excel_ProducesValidNonEmptyWorkbook()
    {
        var xlsx = ListExcelGenerator.ParticipantList(Data(3));
        Assert.True(xlsx.Length > 0);
        // XLSX is a ZIP → "PK" magic bytes.
        Assert.Equal([0x50, 0x4B], xlsx.Take(2).ToArray());

        var food = ListExcelGenerator.FoodPreferenceList(Data(3));
        Assert.True(food.Length > 0);
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
