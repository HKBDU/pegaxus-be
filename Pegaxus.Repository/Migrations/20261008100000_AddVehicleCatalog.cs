using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261008100000_AddVehicleCatalog")]
public partial class AddVehicleCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TYPE vehicle_status ADD VALUE IF NOT EXISTS 'SUSPENDED';", suppressTransaction: true);
        migrationBuilder.Sql("ALTER TYPE vehicle_status ADD VALUE IF NOT EXISTS 'OUT_OF_SERVICE';", suppressTransaction: true);
        ReplaceIndexes(migrationBuilder, "is_deleted = false");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // ponytail: PostgreSQL enum labels remain on downgrade; removing them requires rebuilding the type and dependent columns.
        ReplaceIndexes(migrationBuilder, null);
    }

    private static void ReplaceIndexes(MigrationBuilder migrationBuilder, string? filter)
    {
        migrationBuilder.DropIndex("Vehicle_vehicle_code_key", "Vehicle");
        migrationBuilder.DropIndex("Vehicle_license_plate_key", "Vehicle");
        migrationBuilder.CreateIndex("Vehicle_vehicle_code_key", "Vehicle", "vehicle_code", unique: true, filter: filter);
        migrationBuilder.CreateIndex("Vehicle_license_plate_key", "Vehicle", "license_plate", unique: true, filter: filter);
    }
}
