using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkeroGarden.Web.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddJobRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "JobRunId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_JobRunId",
                table: "AuditLogs",
                column: "JobRunId");

            migrationBuilder.CreateIndex(
                name: "IX_JobRuns_StartedAt",
                table: "JobRuns",
                column: "StartedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_JobRuns_JobRunId",
                table: "AuditLogs",
                column: "JobRunId",
                principalTable: "JobRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_JobRuns_JobRunId",
                table: "AuditLogs");

            migrationBuilder.DropTable(
                name: "JobRuns");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_JobRunId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "JobRunId",
                table: "AuditLogs");
        }
    }
}
