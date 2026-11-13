using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkeroGarden.Web.Core.Migrations
{
    /// <summary>
    /// One menu, and a dish states its diet (client review 2026-11-11, items 6 and 12; SRS §4.5, §8.7).
    /// A dish no longer belongs to lunch or dinner — any dish can be chosen at any sitting — so
    /// <c>MealOptions.MealType</c> is dropped. The vegetarian checkbox becomes one diet value
    /// (None / Vegetarian / Vegan). Hand-written, because it carries data steps EF cannot infer.
    /// </summary>
    public partial class OneMenuWithDishDiet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Diet",
                table: "MealOptions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.Sql(
                """UPDATE "MealOptions" SET "Diet" = 'Vegetarian' WHERE "IsVegetarian";""");

            // Display order used to restart per meal type (lunch 1–5, dinner 1–5); on one menu that
            // would interleave the two. Move dinners after the last lunch so the list reads as it did.
            migrationBuilder.Sql(
                """
                UPDATE "MealOptions"
                SET "DisplayOrder" = "DisplayOrder"
                    + (SELECT COALESCE(MAX("DisplayOrder"), 0) FROM "MealOptions" WHERE "MealType" = 'Lunch')
                WHERE "MealType" = 'Dinner';
                """);

            migrationBuilder.DropColumn(
                name: "IsVegetarian",
                table: "MealOptions");

            migrationBuilder.DropColumn(
                name: "MealType",
                table: "MealOptions");
        }

        /// <inheritdoc />
        /// <remarks>Lossy: a vegan dish comes back vegetarian, and every dish comes back as lunch —
        /// which sitting a dish once belonged to is not kept anywhere after <see cref="Up"/>.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MealType",
                table: "MealOptions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Lunch");

            migrationBuilder.AddColumn<bool>(
                name: "IsVegetarian",
                table: "MealOptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """UPDATE "MealOptions" SET "IsVegetarian" = TRUE WHERE "Diet" <> 'None';""");

            migrationBuilder.DropColumn(
                name: "Diet",
                table: "MealOptions");
        }
    }
}
