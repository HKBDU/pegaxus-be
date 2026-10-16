using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261016090000_AddFlow4Schema")]
public partial class AddFlow4Schema : Migration
{
    private const string OldHandoverComment =
        "HandoverRecord: Một lần bàn giao trong Transport. from_type/to_type xác định bên giao/nhận là CUSTOMER hay EMPLOYEE; from/to_route_plan_route_id xác định chặng vừa kết thúc và chặng tiếp theo.";

    private const string NewHandoverComment =
        "HandoverRecord: Một lần bàn giao trong Transport. from_type/to_type xác định bên giao/nhận là CUSTOMER, EMPLOYEE hay PROVIDER (đối tác vận chuyển); from/to_route_plan_route_id xác định chặng vừa kết thúc và chặng tiếp theo.";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TYPE handover_party_type ADD VALUE IF NOT EXISTS 'PROVIDER';", suppressTransaction: true);

        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:Enum:dossier_type", "MAIN,EXTRA")
            .Annotation("Npgsql:Enum:route_segment_status", "PLANNED,IN_TRANSIT,COMPLETED");

        migrationBuilder.AddColumn<RouteSegmentStatus>(
            name: "status",
            table: "RoutePlanRoute",
            type: "route_segment_status",
            nullable: false,
            defaultValueSql: "'PLANNED'::route_segment_status");
        migrationBuilder.Sql("""
            UPDATE "RoutePlanRoute"
            SET status = (CASE
                WHEN actual_arrival_at IS NOT NULL THEN 'COMPLETED'
                WHEN actual_departure_at IS NOT NULL THEN 'IN_TRANSIT'
                ELSE 'PLANNED' END)::route_segment_status;
            """);

        migrationBuilder.AddColumn<DossierType>(
            name: "dossier_type",
            table: "ComplianceDossier",
            type: "dossier_type",
            nullable: false,
            defaultValueSql: "'MAIN'::dossier_type");
        migrationBuilder.DropIndex(name: "ComplianceDossier_transport_id_key", table: "ComplianceDossier");
        migrationBuilder.CreateIndex(
            name: "ComplianceDossier_transport_id_dossier_type_key",
            table: "ComplianceDossier",
            columns: new[] { "transport_id", "dossier_type" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ComplianceDossier_transport_id_idx",
            table: "ComplianceDossier",
            column: "transport_id");

        migrationBuilder.AlterTable(name: "HandoverRecord", comment: NewHandoverComment, oldComment: OldHandoverComment);
        migrationBuilder.AddColumn<Guid>(name: "from_transport_provider_id", table: "HandoverRecord", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "to_transport_provider_id", table: "HandoverRecord", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(
            name: "HandoverRecord_from_transport_provider_id_idx", table: "HandoverRecord", column: "from_transport_provider_id");
        migrationBuilder.CreateIndex(
            name: "HandoverRecord_to_transport_provider_id_idx", table: "HandoverRecord", column: "to_transport_provider_id");
        migrationBuilder.AddForeignKey(
            name: "HandoverRecord_from_transport_provider_id_fkey", table: "HandoverRecord", column: "from_transport_provider_id",
            principalTable: "TransportProvider", principalColumn: "transport_provider_id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "HandoverRecord_to_transport_provider_id_fkey", table: "HandoverRecord", column: "to_transport_provider_id",
            principalTable: "TransportProvider", principalColumn: "transport_provider_id", onDelete: ReferentialAction.Restrict);

        foreach (var column in new[] { "origin_latitude", "origin_longitude", "destination_latitude", "destination_longitude" })
            migrationBuilder.AddColumn<decimal>(
                name: column, table: "Booking", type: "numeric(10,4)", precision: 10, scale: 4, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // PROVIDER stays in handover_party_type: PostgreSQL cannot drop an enum value without rebuilding the type.
        foreach (var column in new[] { "origin_latitude", "origin_longitude", "destination_latitude", "destination_longitude" })
            migrationBuilder.DropColumn(name: column, table: "Booking");

        migrationBuilder.DropForeignKey(name: "HandoverRecord_from_transport_provider_id_fkey", table: "HandoverRecord");
        migrationBuilder.DropForeignKey(name: "HandoverRecord_to_transport_provider_id_fkey", table: "HandoverRecord");
        migrationBuilder.DropIndex(name: "HandoverRecord_from_transport_provider_id_idx", table: "HandoverRecord");
        migrationBuilder.DropIndex(name: "HandoverRecord_to_transport_provider_id_idx", table: "HandoverRecord");
        migrationBuilder.DropColumn(name: "from_transport_provider_id", table: "HandoverRecord");
        migrationBuilder.DropColumn(name: "to_transport_provider_id", table: "HandoverRecord");
        migrationBuilder.AlterTable(name: "HandoverRecord", comment: OldHandoverComment, oldComment: NewHandoverComment);

        // EXTRA dossiers cannot coexist with the single-dossier unique index; refuse instead of deleting legal records.
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM "ComplianceDossier" WHERE dossier_type = 'EXTRA') THEN
                    RAISE EXCEPTION 'Cannot roll back AddFlow4Schema while EXTRA compliance dossiers exist.';
                END IF;
            END $$;
            """);
        migrationBuilder.DropIndex(name: "ComplianceDossier_transport_id_dossier_type_key", table: "ComplianceDossier");
        migrationBuilder.DropIndex(name: "ComplianceDossier_transport_id_idx", table: "ComplianceDossier");
        migrationBuilder.DropColumn(name: "dossier_type", table: "ComplianceDossier");
        migrationBuilder.CreateIndex(
            name: "ComplianceDossier_transport_id_key", table: "ComplianceDossier", column: "transport_id", unique: true);

        migrationBuilder.DropColumn(name: "status", table: "RoutePlanRoute");

        migrationBuilder.AlterDatabase()
            .OldAnnotation("Npgsql:Enum:dossier_type", "MAIN,EXTRA")
            .OldAnnotation("Npgsql:Enum:route_segment_status", "PLANNED,IN_TRANSIT,COMPLETED");
    }
}
