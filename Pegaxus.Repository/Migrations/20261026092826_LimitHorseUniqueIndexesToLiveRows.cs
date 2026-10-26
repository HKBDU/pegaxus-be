using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pegaxus.Repository.Migrations
{
    /// <inheritdoc />
    public partial class LimitHorseUniqueIndexesToLiveRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "Horse_microchip_number_key",
                table: "Horse");

            migrationBuilder.DropIndex(
                name: "Horse_passport_number_key",
                table: "Horse");

            migrationBuilder.CreateIndex(
                name: "Horse_microchip_number_key",
                table: "Horse",
                column: "microchip_number",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "Horse_passport_number_key",
                table: "Horse",
                column: "passport_number",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "Horse_microchip_number_key",
                table: "Horse");

            migrationBuilder.DropIndex(
                name: "Horse_passport_number_key",
                table: "Horse");

            migrationBuilder.CreateIndex(
                name: "Horse_microchip_number_key",
                table: "Horse",
                column: "microchip_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Horse_passport_number_key",
                table: "Horse",
                column: "passport_number",
                unique: true);
        }
    }
}
