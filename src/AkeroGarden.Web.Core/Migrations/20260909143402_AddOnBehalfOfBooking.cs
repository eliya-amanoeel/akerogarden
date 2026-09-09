using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkeroGarden.Web.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddOnBehalfOfBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfAdminId",
                table: "Bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAttestedAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnBehalfOfAdminId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TermsAttestedAt",
                table: "Bookings");
        }
    }
}
