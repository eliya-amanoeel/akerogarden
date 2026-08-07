using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkeroGarden.Web.Core.Migrations
{
    /// <inheritdoc />
    public partial class MealsTimesAndAllergies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealPreferences");

            migrationBuilder.DropColumn(
                name: "AttendsBreakfast",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "IsFullBoard",
                table: "Participants");

            migrationBuilder.RenameColumn(
                name: "DietaryRequirements",
                table: "Participants",
                newName: "Allergies");

            migrationBuilder.RenameColumn(
                name: "DietaryNotes",
                table: "Participants",
                newName: "AllergyNotes");

            migrationBuilder.AddColumn<bool>(
                name: "ArrivalBreakfast",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ArrivalDinner",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ArrivalLunch",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "CheckInTime",
                table: "Bookings",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "CheckOutTime",
                table: "Bookings",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DayGuestBreakfast",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DayGuestDinner",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DayGuestLunch",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DepartureBreakfast",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DepartureDinner",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DepartureLunch",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "VeganCount",
                table: "Bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VegetarianCount",
                table: "Bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BookingMealSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    MealType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MealOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MealOptionSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingMealSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingMealSelections_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingMealSelections_MealOptions_MealOptionId",
                        column: x => x.MealOptionId,
                        principalTable: "MealOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingMealSelections_BookingId_Date_MealType",
                table: "BookingMealSelections",
                columns: new[] { "BookingId", "Date", "MealType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingMealSelections_MealOptionId",
                table: "BookingMealSelections",
                column: "MealOptionId");

            // Hand-added, not scaffolded. The renamed column still holds DietaryRequirement names,
            // and Enum.Parse<Allergy> throws on every one that no longer exists — which would take
            // down any page loading a participant, not merely lose the value. Translate the four
            // that survived, and drop Vegetarian/Vegan: they became per-booking portion counts
            // (§4.6) and cannot be reconstructed per person, so there is nowhere for them to go.
            migrationBuilder.Sql("""
                UPDATE "Participants" SET "Allergies" = trim(both ',' from regexp_replace(
                    replace(replace(replace(replace(replace(
                        "Allergies",
                        'GlutenFree', 'Gluten'),
                        'LactoseFree', 'Lactose'),
                        'NutAllergy', 'Nut'),
                        'Vegetarian', ''),
                        'Vegan', ''),
                    ',{2,}', ',', 'g'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingMealSelections");

            migrationBuilder.DropColumn(
                name: "ArrivalBreakfast",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ArrivalDinner",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ArrivalLunch",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckInTime",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckOutTime",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DayGuestBreakfast",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DayGuestDinner",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DayGuestLunch",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DepartureBreakfast",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DepartureDinner",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DepartureLunch",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VeganCount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VegetarianCount",
                table: "Bookings");

            migrationBuilder.RenameColumn(
                name: "AllergyNotes",
                table: "Participants",
                newName: "DietaryNotes");

            migrationBuilder.RenameColumn(
                name: "Allergies",
                table: "Participants",
                newName: "DietaryRequirements");

            migrationBuilder.AddColumn<bool>(
                name: "AttendsBreakfast",
                table: "Participants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFullBoard",
                table: "Participants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MealPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MealOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    MealOptionSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MealType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealPreferences_MealOptions_MealOptionId",
                        column: x => x.MealOptionId,
                        principalTable: "MealOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealPreferences_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferences_MealOptionId",
                table: "MealPreferences",
                column: "MealOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_MealPreferences_ParticipantId",
                table: "MealPreferences",
                column: "ParticipantId");
        }
    }
}
