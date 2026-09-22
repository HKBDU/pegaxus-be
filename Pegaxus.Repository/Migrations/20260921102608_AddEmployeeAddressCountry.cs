using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pegaxus.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeAddressCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "Employee",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country",
                table: "Employee",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "country",
                table: "Employee");
        }
    }
}
