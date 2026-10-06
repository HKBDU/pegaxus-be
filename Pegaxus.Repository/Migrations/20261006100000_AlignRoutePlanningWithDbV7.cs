using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261006100000_AlignRoutePlanningWithDbV7")]
public partial class AlignRoutePlanningWithDbV7 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("RoutePlan_transport_plan_id_key", "RoutePlan");
        migrationBuilder.CreateIndex("RoutePlan_transport_plan_id_idx", "RoutePlan", "transport_plan_id");

        migrationBuilder.AddColumn<Guid>("escort_employee_id", "RoutePlanRoute", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>("transport_provider_id", "RoutePlanRoute", type: "uuid", nullable: true);

        migrationBuilder.CreateIndex("RoutePlanRoute_escort_employee_id_idx", "RoutePlanRoute", "escort_employee_id");
        migrationBuilder.CreateIndex("RoutePlanRoute_transport_provider_id_idx", "RoutePlanRoute", "transport_provider_id");

        migrationBuilder.AddForeignKey("RoutePlanRoute_escort_employee_id_fkey", "RoutePlanRoute", "escort_employee_id",
            "Employee", "employee_id", onDelete: ReferentialAction.NoAction);
        migrationBuilder.AddForeignKey("RoutePlanRoute_transport_provider_id_fkey", "RoutePlanRoute", "transport_provider_id",
            "TransportProvider", "transport_provider_id", onDelete: ReferentialAction.NoAction);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("RoutePlanRoute_escort_employee_id_fkey", "RoutePlanRoute");
        migrationBuilder.DropForeignKey("RoutePlanRoute_transport_provider_id_fkey", "RoutePlanRoute");
        migrationBuilder.DropIndex("RoutePlanRoute_escort_employee_id_idx", "RoutePlanRoute");
        migrationBuilder.DropIndex("RoutePlanRoute_transport_provider_id_idx", "RoutePlanRoute");
        migrationBuilder.DropColumn("escort_employee_id", "RoutePlanRoute");
        migrationBuilder.DropColumn("transport_provider_id", "RoutePlanRoute");
        migrationBuilder.DropIndex("RoutePlan_transport_plan_id_idx", "RoutePlan");
        migrationBuilder.CreateIndex("RoutePlan_transport_plan_id_key", "RoutePlan", "transport_plan_id", unique: true);
    }
}
