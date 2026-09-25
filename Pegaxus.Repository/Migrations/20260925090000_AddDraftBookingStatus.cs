using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pegaxus.Repository;

#nullable disable

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260925090000_AddDraftBookingStatus")]
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
