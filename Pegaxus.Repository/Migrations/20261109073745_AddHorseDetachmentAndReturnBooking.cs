using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pegaxus.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddHorseDetachmentAndReturnBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "detached_at",
                table: "BookingHorse",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "detached_incident_report_id",
                table: "BookingHorse",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "detached_location_id",
                table: "BookingHorse",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "return_of_transport_id",
                table: "Booking",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingHorse_detached_incident_report_id",
                table: "BookingHorse",
                column: "detached_incident_report_id");

            migrationBuilder.CreateIndex(
                name: "IX_BookingHorse_detached_location_id",
                table: "BookingHorse",
                column: "detached_location_id");

            migrationBuilder.CreateIndex(
                name: "Booking_return_of_transport_id_idx",
                table: "Booking",
                column: "return_of_transport_id");

            migrationBuilder.AddForeignKey(
                name: "Booking_return_of_transport_id_fkey",
                table: "Booking",
                column: "return_of_transport_id",
                principalTable: "Transport",
                principalColumn: "transport_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "BookingHorse_detached_incident_report_id_fkey",
                table: "BookingHorse",
                column: "detached_incident_report_id",
                principalTable: "IncidentReport",
                principalColumn: "incident_report_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "BookingHorse_detached_location_id_fkey",
                table: "BookingHorse",
                column: "detached_location_id",
                principalTable: "Location",
                principalColumn: "location_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "Booking_return_of_transport_id_fkey",
                table: "Booking");

            migrationBuilder.DropForeignKey(
                name: "BookingHorse_detached_incident_report_id_fkey",
                table: "BookingHorse");

            migrationBuilder.DropForeignKey(
                name: "BookingHorse_detached_location_id_fkey",
                table: "BookingHorse");

            migrationBuilder.DropIndex(
                name: "IX_BookingHorse_detached_incident_report_id",
                table: "BookingHorse");

            migrationBuilder.DropIndex(
                name: "IX_BookingHorse_detached_location_id",
                table: "BookingHorse");

            migrationBuilder.DropIndex(
                name: "Booking_return_of_transport_id_idx",
                table: "Booking");

            migrationBuilder.DropColumn(
                name: "detached_at",
                table: "BookingHorse");

            migrationBuilder.DropColumn(
                name: "detached_incident_report_id",
                table: "BookingHorse");

            migrationBuilder.DropColumn(
                name: "detached_location_id",
                table: "BookingHorse");

            migrationBuilder.DropColumn(
                name: "return_of_transport_id",
                table: "Booking");
        }
    }
}
