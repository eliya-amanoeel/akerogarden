using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkeroGarden.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderSentAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderEmailSentAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CheckIn",
                table: "Bookings",
                column: "CheckIn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_CheckIn",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ReminderEmailSentAt",
                table: "Bookings");
        }
    }
}
