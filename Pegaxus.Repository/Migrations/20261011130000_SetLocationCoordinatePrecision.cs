using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261011130000_SetLocationCoordinatePrecision")]
public partial class SetLocationCoordinatePrecision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "latitude",
            table: "Location",
            type: "numeric(10,4)",
            precision: 10,
            scale: 4,
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(10,7)",
            oldPrecision: 10,
            oldScale: 7);

        migrationBuilder.AlterColumn<decimal>(
            name: "longitude",
            table: "Location",
            type: "numeric(10,4)",
            precision: 10,
            scale: 4,
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(10,7)",
            oldPrecision: 10,
            oldScale: 7);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "latitude",
            table: "Location",
            type: "numeric(10,7)",
            precision: 10,
            scale: 7,
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(10,4)",
            oldPrecision: 10,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "longitude",
            table: "Location",
            type: "numeric(10,7)",
            precision: 10,
            scale: 7,
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(10,4)",
            oldPrecision: 10,
            oldScale: 4);
    }
}
