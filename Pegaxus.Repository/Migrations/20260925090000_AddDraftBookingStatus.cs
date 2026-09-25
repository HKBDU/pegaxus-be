using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pegaxus.Repository.Migrations;

public partial class AddDraftBookingStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TYPE booking_status ADD VALUE IF NOT EXISTS 'DRAFT' BEFORE 'PENDING';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // PostgreSQL does not support removing an enum value safely.
    }
}
