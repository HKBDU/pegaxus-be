using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261015090000_AddTransportPlanLocations")]
public partial class AddTransportPlanLocations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("origin_location_id", "TransportPlan", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>("destination_location_id", "TransportPlan", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex("TransportPlan_origin_location_id_idx", "TransportPlan", "origin_location_id");
        migrationBuilder.CreateIndex("TransportPlan_destination_location_id_idx", "TransportPlan", "destination_location_id");
        migrationBuilder.AddForeignKey("TransportPlan_origin_location_id_fkey", "TransportPlan", "origin_location_id",
            "Location", principalColumn: "location_id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("TransportPlan_destination_location_id_fkey", "TransportPlan", "destination_location_id",
            "Location", principalColumn: "location_id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("TransportPlan_origin_location_id_fkey", "TransportPlan");
        migrationBuilder.DropForeignKey("TransportPlan_destination_location_id_fkey", "TransportPlan");
        migrationBuilder.DropIndex("TransportPlan_origin_location_id_idx", "TransportPlan");
        migrationBuilder.DropIndex("TransportPlan_destination_location_id_idx", "TransportPlan");
        migrationBuilder.DropColumn("origin_location_id", "TransportPlan");
        migrationBuilder.DropColumn("destination_location_id", "TransportPlan");
    }
}
