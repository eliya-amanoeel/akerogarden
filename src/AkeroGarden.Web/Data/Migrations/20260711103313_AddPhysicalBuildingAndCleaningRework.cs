using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkeroGarden.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhysicalBuildingAndCleaningRework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PhysicalBuildingId",
                table: "Facilities",
                type: "uuid",
                nullable: true);

            // Existing CleaningZone rows (staging demo data) backfill to "Everything" rather than
            // an empty string — the enum-as-string conversion would throw on read for "" the next
            // time such a row is queried. DemoDataSeeder replaces all of this on its next reset;
            // this default only needs to be valid in the meantime.
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "CleaningZones",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Everything");

            migrationBuilder.AddColumn<Guid>(
                name: "PhysicalBuildingId",
                table: "CleaningZones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PhysicalBuildingId",
                table: "Buildings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "ConferenceCleaningPurchased",
                table: "Bookings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MainBuildingCleaningPurchased",
                table: "Bookings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SleepingCleaningPurchased",
                table: "Bookings",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PhysicalBuildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalBuildings", x => x.Id);
                });

            // Placeholder row matching the all-zero-GUID default that any pre-existing Buildings
            // row just backfilled into PhysicalBuildingId — without this, the FK constraint added
            // below fails on any environment (e.g. staging) that already has Building rows.
            // DemoDataSeeder wipes and replaces this on its next reset; this only bridges the gap
            // until then.
            migrationBuilder.InsertData(
                table: "PhysicalBuildings",
                columns: new[] { "Id", "Name", "Description", "IsActive", "DisplayOrder" },
                values: new object[]
                {
                    Guid.Empty, "Ej tilldelad", "Tillfällig platshållare — tilldela byggnaden en riktig fysisk byggnad.", false, 999
                });

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_PhysicalBuildingId",
                table: "Facilities",
                column: "PhysicalBuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_CleaningZones_PhysicalBuildingId",
                table: "CleaningZones",
                column: "PhysicalBuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_PhysicalBuildingId",
                table: "Buildings",
                column: "PhysicalBuildingId");

            migrationBuilder.AddForeignKey(
                name: "FK_Buildings_PhysicalBuildings_PhysicalBuildingId",
                table: "Buildings",
                column: "PhysicalBuildingId",
                principalTable: "PhysicalBuildings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CleaningZones_PhysicalBuildings_PhysicalBuildingId",
                table: "CleaningZones",
                column: "PhysicalBuildingId",
                principalTable: "PhysicalBuildings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Facilities_PhysicalBuildings_PhysicalBuildingId",
                table: "Facilities",
                column: "PhysicalBuildingId",
                principalTable: "PhysicalBuildings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Buildings_PhysicalBuildings_PhysicalBuildingId",
                table: "Buildings");

            migrationBuilder.DropForeignKey(
                name: "FK_CleaningZones_PhysicalBuildings_PhysicalBuildingId",
                table: "CleaningZones");

            migrationBuilder.DropForeignKey(
                name: "FK_Facilities_PhysicalBuildings_PhysicalBuildingId",
                table: "Facilities");

            migrationBuilder.DropTable(
                name: "PhysicalBuildings");

            migrationBuilder.DropIndex(
                name: "IX_Facilities_PhysicalBuildingId",
                table: "Facilities");

            migrationBuilder.DropIndex(
                name: "IX_CleaningZones_PhysicalBuildingId",
                table: "CleaningZones");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_PhysicalBuildingId",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "PhysicalBuildingId",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "CleaningZones");

            migrationBuilder.DropColumn(
                name: "PhysicalBuildingId",
                table: "CleaningZones");

            migrationBuilder.DropColumn(
                name: "PhysicalBuildingId",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "ConferenceCleaningPurchased",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "MainBuildingCleaningPurchased",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "SleepingCleaningPurchased",
                table: "Bookings");
        }
    }
}
