using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pegaxus.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "country",
                table: "Vehicle",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "country",
                table: "Vehicle");
        }
    }
}
