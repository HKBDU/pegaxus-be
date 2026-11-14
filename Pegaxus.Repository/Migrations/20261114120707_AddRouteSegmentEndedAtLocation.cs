using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pegaxus.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteSegmentEndedAtLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ended_at_location_id",
                table: "RoutePlanRoute",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "RoutePlanRoute_ended_at_location_id_idx",
                table: "RoutePlanRoute",
                column: "ended_at_location_id");

            migrationBuilder.AddForeignKey(
                name: "RoutePlanRoute_ended_at_location_id_fkey",
                table: "RoutePlanRoute",
                column: "ended_at_location_id",
                principalTable: "Location",
                principalColumn: "location_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "RoutePlanRoute_ended_at_location_id_fkey",
                table: "RoutePlanRoute");

            migrationBuilder.DropIndex(
                name: "RoutePlanRoute_ended_at_location_id_idx",
                table: "RoutePlanRoute");

            migrationBuilder.DropColumn(
                name: "ended_at_location_id",
                table: "RoutePlanRoute");
        }
    }
}
