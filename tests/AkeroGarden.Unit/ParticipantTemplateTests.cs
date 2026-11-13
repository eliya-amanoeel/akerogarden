using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using ClosedXML.Excel;

namespace AkeroGarden.Unit;

/// <summary>
/// The per-booking participant template (FR-118): its shape follows the booking, and its dropdowns
/// say exactly what the completion form says. The file's own validation is a convenience the import
/// re-checks (see <see cref="ParticipantImportTests"/>); what is asserted here is that the guest is
/// handed the right file.
/// </summary>
public class ParticipantTemplateTests
{
    private static readonly DateOnly CheckIn = new(2027, 6, 11);

    private static ParticipantTemplateShape Shape(int overnight = 3, bool dayGuests = true) =>
        new(overnight, [CheckIn, CheckIn.AddDays(1), CheckIn.AddDays(2)], dayGuests);

    private static XLWorkbook Open(byte[] xlsx) => new(new MemoryStream(xlsx));

    [Fact]
    public void Overnight_sheet_has_exactly_the_bookings_rows_numbered_from_one()
    {
        using var workbook = Open(ParticipantTemplate.Build(Shape(overnight: 3)));
        var sheet = workbook.Worksheet(ParticipantTemplate.OvernightSheet);

        var numbers = Enumerable.Range(ParticipantTemplate.HeaderRow + 1, 4)
            .Select(r => sheet.Cell(r, ParticipantTemplate.NumberColumn).GetString())
            .ToList();
        Assert.Equal(["1", "2", "3", ""], numbers);
    }

    [Fact]
    public void Both_sheets_carry_the_headers_the_import_expects()
    {
        using var workbook = Open(ParticipantTemplate.Build(Shape()));

        foreach (var (sheet, dayGuest) in new[] { (ParticipantTemplate.OvernightSheet, false), (ParticipantTemplate.DayGuestSheet, true) })
        {
            var headers = ParticipantTemplate.Headers(dayGuest);
            var written = Enumerable.Range(1, headers.Count)
                .Select(c => workbook.Worksheet(sheet).Cell(ParticipantTemplate.HeaderRow, c).GetString())
                .ToList();
            Assert.Equal(headers, written);
        }
    }

    [Fact]
    public void Day_guest_sheet_exists_only_for_a_booking_with_day_guests()
    {
        // §4.2: day guests exist only on a full-board booking.
        using var withDayGuests = Open(ParticipantTemplate.Build(Shape(dayGuests: true)));
        using var without = Open(ParticipantTemplate.Build(Shape(dayGuests: false)));

        Assert.True(withDayGuests.Worksheets.Contains(ParticipantTemplate.DayGuestSheet));
        Assert.False(without.Worksheets.Contains(ParticipantTemplate.DayGuestSheet));
    }

    [Fact]
    public void Day_guest_sheet_offers_the_fixed_number_of_rows()
    {
        using var workbook = Open(ParticipantTemplate.Build(Shape()));
        var sheet = workbook.Worksheet(ParticipantTemplate.DayGuestSheet);

        var last = ParticipantTemplate.HeaderRow + ParticipantTemplate.DayGuestRows;
        Assert.Equal(ParticipantTemplate.DayGuestRows.ToString(), sheet.Cell(last, ParticipantTemplate.NumberColumn).GetString());
        Assert.Equal("", sheet.Cell(last + 1, ParticipantTemplate.NumberColumn).GetString());
    }

    [Fact]
    public void Age_dropdown_lists_exactly_the_forms_age_labels()
    {
        // The form's age select lists Enum.GetValues<AgeCategory>() through BookingDisplay.AgeLabel —
        // the template must offer the same words in the same order, or a guest picks a label the
        // form does not know.
        using var workbook = Open(ParticipantTemplate.Build(Shape()));
        var lists = workbook.Worksheet(ParticipantTemplate.ListsSheet);

        var formLabels = Enum.GetValues<AgeCategory>().Select(BookingDisplay.AgeLabel).ToList();
        var offered = Enumerable.Range(1, formLabels.Count + 1).Select(r => lists.Cell(r, 1).GetString()).ToList();

        Assert.Equal([.. formLabels, ""], offered);
        Assert.Equal(["Vuxen (4+)", "Barn (0–3)"], formLabels);
    }

    [Fact]
    public void Day_dropdown_lists_the_stay_days_named_as_the_form_names_them()
    {
        using var workbook = Open(ParticipantTemplate.Build(Shape()));
        var lists = workbook.Worksheet(ParticipantTemplate.ListsSheet);

        var offered = Enumerable.Range(1, 4).Select(r => lists.Cell(r, 2).GetString()).ToList();
        Assert.Equal(["Fredag 11 juni", "Lördag 12 juni", "Söndag 13 juni", ""], offered);
    }

    [Fact]
    public void Lists_sheet_is_hidden_and_input_sheets_are_protected()
    {
        using var workbook = Open(ParticipantTemplate.Build(Shape()));

        Assert.Equal(XLWorksheetVisibility.Hidden, workbook.Worksheet(ParticipantTemplate.ListsSheet).Visibility);
        Assert.True(workbook.Worksheet(ParticipantTemplate.OvernightSheet).IsProtected);
        Assert.True(workbook.Worksheet(ParticipantTemplate.DayGuestSheet).IsProtected);
    }

    [Fact]
    public void Only_the_input_cells_are_unlocked()
    {
        using var workbook = Open(ParticipantTemplate.Build(Shape()));
        var sheet = workbook.Worksheet(ParticipantTemplate.OvernightSheet);
        var firstRow = ParticipantTemplate.HeaderRow + 1;

        Assert.False(sheet.Cell(firstRow, ParticipantTemplate.NameColumn).Style.Protection.Locked);
        Assert.False(sheet.Cell(firstRow, ParticipantTemplate.NotesColumn(false)).Style.Protection.Locked);
        Assert.True(sheet.Cell(firstRow, ParticipantTemplate.NumberColumn).Style.Protection.Locked);
        Assert.True(sheet.Cell(ParticipantTemplate.HeaderRow, ParticipantTemplate.NameColumn).Style.Protection.Locked);
    }

    [Fact]
    public void The_file_declares_its_dropdowns_and_rules_as_written_formulas()
    {
        // COVERAGE LIMIT, stated: whether Excel then enforces these is Excel's behaviour and is
        // checked by hand (review record, manual test). What is asserted here is that the file
        // carries them, re-read from the saved bytes — in particular that the rule formulas were
        // written as formulas Excel can read (a data-validation formula has no leading "=").
        using var workbook = Open(ParticipantTemplate.Build(Shape()));
        var sheet = workbook.Worksheet(ParticipantTemplate.DayGuestSheet);
        var row = ParticipantTemplate.HeaderRow + 1;

        var name = sheet.Cell(row, ParticipantTemplate.NameColumn).GetDataValidation();
        Assert.Equal(XLAllowedValues.Custom, name.AllowedValues);
        Assert.Contains("ISTEXT(B5)", name.MinValue, StringComparison.Ordinal);
        Assert.False(name.MinValue.StartsWith('='));

        var age = sheet.Cell(row, ParticipantTemplate.AgeColumn).GetDataValidation();
        Assert.Equal(XLAllowedValues.List, age.AllowedValues);
        Assert.Contains(ParticipantTemplate.ListsSheet, age.MinValue, StringComparison.Ordinal);

        var day = sheet.Cell(row, ParticipantTemplate.DayColumn).GetDataValidation();
        Assert.Equal(XLAllowedValues.List, day.AllowedValues);

        var allergy = sheet.Cell(row, ParticipantTemplate.FirstAllergyColumn(true)).GetDataValidation();
        Assert.Equal(XLAllowedValues.List, allergy.AllowedValues);

        var notes = sheet.Cell(row, ParticipantTemplate.NotesColumn(true)).GetDataValidation();
        Assert.Equal(XLAllowedValues.TextLength, notes.AllowedValues);

        // The red-while-missing rule: an expression, written as a formula rather than as text.
        var red = Assert.Single(sheet.ConditionalFormats);
        Assert.Equal(XLConditionalFormatType.Expression, red.ConditionalFormatType);
        Assert.True(red.Values[1].IsFormula);
        Assert.Contains("LEN(TRIM(B5))=0", red.Values[1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Age_starts_empty_rather_than_defaulting_to_adult()
    {
        // Decision 2026-11-13: unlike the form's "Vuxen" default, an unanswered age in the file is
        // visible (red) and refused on import, so a child cannot slip through as an adult.
        using var workbook = Open(ParticipantTemplate.Build(Shape()));
        var sheet = workbook.Worksheet(ParticipantTemplate.OvernightSheet);

        // The value, not IsEmpty(): the cell deliberately carries a dropdown and a red rule, and
        // IsEmpty() counts those as content.
        Assert.True(sheet.Cell(ParticipantTemplate.HeaderRow + 1, ParticipantTemplate.AgeColumn).Value.IsBlank);
    }

    [Fact]
    public void Shape_for_a_booking_takes_its_count_days_and_meal_plan()
    {
        var booking = new Booking
        {
            OvernightGuestCount = 12,
            CheckIn = CheckIn,
            CheckOut = CheckIn.AddDays(2),
            MealPlan = MealPlan.SelfCateringAll,
        };

        var shape = ParticipantTemplateShape.For(booking);

        Assert.Equal(12, shape.OvernightCount);
        Assert.Equal([CheckIn, CheckIn.AddDays(1), CheckIn.AddDays(2)], shape.StayDates);
        Assert.False(shape.IncludesDayGuests);

        booking.MealPlan = MealPlan.FullBoardAll;
        Assert.True(ParticipantTemplateShape.For(booking).IncludesDayGuests);
    }
}
