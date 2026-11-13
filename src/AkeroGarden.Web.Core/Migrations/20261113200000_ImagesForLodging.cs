using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkeroGarden.Web.Core.Migrations
{
    /// <summary>
    /// One image per lodging building and per room (client review 2026-11-11, item 13; FR-13, FR-17,
    /// FR-81), with facilities, buildings and rooms storing their image the same way.
    /// <list type="bullet">
    /// <item><c>Facilities.ImageUrl</c> becomes <c>ImageName</c>. It has held the bare blob name since
    /// the post-launch security review; any row still holding a pre-review absolute Blob URL is
    /// reduced to its last path segment first (exactly the name), and anything that is then still not a
    /// name the app generates is cleared — the page could never have rendered it.</item>
    /// <item><c>Buildings.ImageName</c> and <c>Rooms.ImageName</c> are added, empty.</item>
    /// <item><c>Buildings.Description</c> is dropped. The public page shows a house's
    /// <c>SharedFacilities</c> and derives its room and bed counts from the rooms, which is all the
    /// description ever restated. <b>Lossy:</b> check production's descriptions before deploying.</item>
    /// </list>
    /// Hand-written, because EF cannot infer the data conversion that has to run between the rename
    /// and the narrowing of the column.
    /// </summary>
    public partial class ImagesForLodging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ImageUrl",
                table: "Facilities",
                newName: "ImageName");

            // Legacy absolute URL → its last path segment, query and fragment stripped first.
            migrationBuilder.Sql(
                """
                UPDATE "Facilities"
                SET "ImageName" = regexp_replace(split_part(split_part("ImageName", '?', 1), '#', 1), '^.*/', '')
                WHERE "ImageName" LIKE '%://%';
                """);

            // Same allow-list as ImagePath.IsValidName — and it must run before the column narrows to 64.
            migrationBuilder.Sql(
                """
                UPDATE "Facilities"
                SET "ImageName" = NULL
                WHERE "ImageName" IS NOT NULL
                  AND "ImageName" !~* '^[0-9a-f]{32}\.(jpg|jpeg|png|webp)$';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ImageName",
                table: "Facilities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageName",
                table: "Buildings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageName",
                table: "Rooms",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Buildings");
        }

        /// <inheritdoc />
        /// <remarks>Lossy: building and room images are dropped, and building descriptions come back empty.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Buildings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.DropColumn(
                name: "ImageName",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "ImageName",
                table: "Buildings");

            migrationBuilder.AlterColumn<string>(
                name: "ImageName",
                table: "Facilities",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "ImageName",
                table: "Facilities",
                newName: "ImageUrl");
        }
    }
}
