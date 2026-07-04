using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkeroGarden.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipantAndMealPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Participants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParticipantType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AgeCategory = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttendanceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoomAssignmentSnapshot = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsFullBoard = table.Column<bool>(type: "boolean", nullable: false),
                    AttendsBreakfast = table.Column<bool>(type: "boolean", nullable: false),
                    DietaryRequirements = table.Column<string>(type: "text", nullable: false),
                    DietaryNotes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Participants_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Participants_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    MealType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MealOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MealOptionSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Participants_BookingId",
                table: "Participants",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Participants_RoomId",
                table: "Participants",
                column: "RoomId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealPreferences");

            migrationBuilder.DropTable(
                name: "Participants");
        }
    }
}
