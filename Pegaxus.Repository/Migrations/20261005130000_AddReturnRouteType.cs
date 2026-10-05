using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261005130000_AddReturnRouteType")]
public partial class AddReturnRouteType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TYPE route_type ADD VALUE IF NOT EXISTS 'RETURN';", suppressTransaction: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("RETURN cannot be removed from a PostgreSQL enum without rebuilding the type.");
}
