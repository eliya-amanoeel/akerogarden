using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkeroGarden.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingLifecycleEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlockedPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CheckIn = table.Column<DateOnly>(type: "date", nullable: true),
                    CheckOut = table.Column<DateOnly>(type: "date", nullable: true),
                    OvernightGuestCount = table.Column<int>(type: "integer", nullable: true),
                    DayGuestCount = table.Column<int>(type: "integer", nullable: true),
                    MealPlan = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    GuestChoosesRooms = table.Column<bool>(type: "boolean", nullable: false),
                    IsOrganisation = table.Column<bool>(type: "boolean", nullable: false),
                    OrgName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OrgNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    InvoiceAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Message = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    DenialReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CompletionToken = table.Column<string>(type: "text", nullable: true),
                    CompletionTokenExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DepositInvoiced = table.Column<bool>(type: "boolean", nullable: false),
                    DepositPaid = table.Column<bool>(type: "boolean", nullable: false),
                    FinalInvoiced = table.Column<bool>(type: "boolean", nullable: false),
                    FinalPaid = table.Column<bool>(type: "boolean", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationInitiator = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeleteAfter = table.Column<DateOnly>(type: "date", nullable: true),
                    DeletionWarningEmailSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BookingCleaningSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CleaningZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AmountExVatSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRateSnapshot = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingCleaningSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingCleaningSelections_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingCleaningSelections_CleaningZones_CleaningZoneId",
                        column: x => x.CleaningZoneId,
                        principalTable: "CleaningZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingFacilitySelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AmountExVatSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRateSnapshot = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingFacilitySelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingFacilitySelections_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingFacilitySelections_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedPeriods_StartDate_EndDate",
                table: "BlockedPeriods",
                columns: new[] { "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingCleaningSelections_BookingId",
                table: "BookingCleaningSelections",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingCleaningSelections_CleaningZoneId",
                table: "BookingCleaningSelections",
                column: "CleaningZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingFacilitySelections_BookingId",
                table: "BookingFacilitySelections",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingFacilitySelections_FacilityId",
                table: "BookingFacilitySelections",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingType",
                table: "Bookings",
                column: "BookingType");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_DeleteAfter",
                table: "Bookings",
                column: "DeleteAfter");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status",
                table: "Bookings",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockedPeriods");

            migrationBuilder.DropTable(
                name: "BookingCleaningSelections");

            migrationBuilder.DropTable(
                name: "BookingFacilitySelections");

            migrationBuilder.DropTable(
                name: "Bookings");
        }
    }
}
