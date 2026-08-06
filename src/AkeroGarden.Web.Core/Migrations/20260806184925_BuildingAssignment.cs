using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkeroGarden.Web.Core.Migrations
{
    /// <inheritdoc />
    public partial class BuildingAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Participants_Rooms_RoomId",
                table: "Participants");

            migrationBuilder.DropIndex(
                name: "IX_Participants_RoomId",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "RoomAssignmentSnapshot",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "GuestChoosesRooms",
                table: "Bookings");

            migrationBuilder.CreateTable(
                name: "BookingBuildingAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PhysicalBuildingNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingBuildingAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingBuildingAssignments_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingBuildingAssignments_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingBuildingAssignments_BookingId_BuildingId",
                table: "BookingBuildingAssignments",
                columns: new[] { "BookingId", "BuildingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingBuildingAssignments_BuildingId",
                table: "BookingBuildingAssignments",
                column: "BuildingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingBuildingAssignments");

            migrationBuilder.AddColumn<string>(
                name: "RoomAssignmentSnapshot",
                table: "Participants",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoomId",
                table: "Participants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GuestChoosesRooms",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Participants_RoomId",
                table: "Participants",
                column: "RoomId");

            migrationBuilder.AddForeignKey(
                name: "FK_Participants_Rooms_RoomId",
                table: "Participants",
                column: "RoomId",
                principalTable: "Rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
