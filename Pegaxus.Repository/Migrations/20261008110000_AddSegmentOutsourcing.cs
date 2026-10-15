using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261008110000_AddSegmentOutsourcing")]
public sealed class AddSegmentOutsourcing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var status in new[] { "PENDING_LEGAL_REVIEW", "PENDING_MANAGER_APPROVAL", "REJECTED_BY_MANAGER", "APPROVED" })
            migrationBuilder.Sql($"ALTER TYPE route_plan_status ADD VALUE IF NOT EXISTS '{status}'", suppressTransaction: true);
        migrationBuilder.AddColumn<string>("service_type", "RoutePlanRoute", type: "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("provider_reference", "RoutePlanRoute", type: "character varying(255)", maxLength: 255, nullable: true);
        migrationBuilder.AddColumn<decimal>("outsourcing_cost_amount", "RoutePlanRoute", type: "numeric(18,2)", precision: 18, scale: 2, nullable: true);
        migrationBuilder.AddColumn<string>("outsourcing_currency", "RoutePlanRoute", type: "character varying(3)", maxLength: 3, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // ponytail: Down retains enum labels; rebuild the enum only after migrating routes that use them.
        migrationBuilder.DropColumn("outsourcing_currency", "RoutePlanRoute");
        migrationBuilder.DropColumn("outsourcing_cost_amount", "RoutePlanRoute");
        migrationBuilder.DropColumn("provider_reference", "RoutePlanRoute");
        migrationBuilder.DropColumn("service_type", "RoutePlanRoute");
    }
}
