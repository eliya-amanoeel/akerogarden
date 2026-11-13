using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AkeroGarden.Web.Core.Migrations
{
    /// <summary>
    /// Two age groups (client review 2026-11-11, item 17; SRS §8.5): <c>Child</c> is 0–3 and free,
    /// <c>Adult</c> is 4 and over. The old 4–12 band joins Adult and the old 0–3 band (<c>Infant</c>)
    /// becomes Child — the line the client prices by has always been age 4, so the mapping is exact.
    /// Data only: the column stays a string, so the model is unchanged. Hand-written, because EF
    /// cannot infer a change of meaning inside a stored value.
    /// </summary>
    public partial class TwoAgeGroups : Migration
    {
        /// <inheritdoc />
        /// <remarks>Order matters: the old children become adults <b>before</b> the old infants take
        /// the name Child, or the infants would be swept into Adult with them.</remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE "Participants" SET "AgeCategory" = 'Adult' WHERE "AgeCategory" = 'Child';""");

            migrationBuilder.Sql(
                """UPDATE "Participants" SET "AgeCategory" = 'Child' WHERE "AgeCategory" = 'Infant';""");
        }

        /// <inheritdoc />
        /// <remarks>Lossy: the 4–12 band is not kept anywhere after <see cref="Up"/>, so every adult
        /// comes back as an adult and only the 0–3 group is restored.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE "Participants" SET "AgeCategory" = 'Infant' WHERE "AgeCategory" = 'Child';""");
        }
    }
}
