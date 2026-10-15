using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

/// <summary>
/// RouteChangeRequest was created by EmergencyRouteChanges but never mapped or used by the
/// Route Change service (requests are carried by Notification and AuditDossier instead).
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261014090000_DropRouteChangeRequest")]
public sealed class DropRouteChangeRequest : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "RoutePlanRoute" DROP CONSTRAINT IF EXISTS "FK_RoutePlanRoute_RouteChangeRequest_superseded_by_change_request~";
            DROP INDEX IF EXISTS "IX_RoutePlanRoute_superseded_by_change_request_id";
            ALTER TABLE "RoutePlanRoute" DROP COLUMN IF EXISTS superseded_by_change_request_id;
            DROP TABLE IF EXISTS "RouteChangeRequest";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "RouteChangeRequest" (
                route_change_request_id uuid PRIMARY KEY,
                route_plan_id uuid NOT NULL REFERENCES "RoutePlan"(route_plan_id) ON DELETE RESTRICT,
                backup_route_plan_id uuid REFERENCES "RoutePlan"(route_plan_id) ON DELETE RESTRICT,
                status varchar(30) NOT NULL,
                reason text NOT NULL,
                client_request_id uuid,
                submitted_request jsonb NOT NULL,
                note text,
                new_estimated_arrival_at timestamp without time zone NOT NULL,
                estimated_extra_cost numeric(18,2),
                currency varchar(3),
                current_segments jsonb NOT NULL,
                proposed_segments jsonb NOT NULL,
                comparison jsonb NOT NULL,
                approval_reasons text[] NOT NULL,
                requested_by_employee_id uuid NOT NULL REFERENCES "Employee"(employee_id) ON DELETE RESTRICT,
                decided_by_employee_id uuid REFERENCES "Employee"(employee_id) ON DELETE RESTRICT,
                decision_note text,
                decided_at timestamp without time zone,
                decision_request jsonb,
                decision_response jsonb,
                incident_report_id uuid REFERENCES "IncidentReport"(incident_report_id) ON DELETE RESTRICT,
                created_at timestamp without time zone NOT NULL,
                updated_at timestamp without time zone NOT NULL,
                CONSTRAINT "CK_RouteChangeRequest_Status" CHECK (status IN ('PENDING', 'APPROVED', 'REJECTED', 'EMERGENCY_RETURN'))
            );
            CREATE UNIQUE INDEX "RouteChangeRequest_pending_route_key" ON "RouteChangeRequest"(route_plan_id) WHERE status = 'PENDING';
            CREATE UNIQUE INDEX "RouteChangeRequest_client_request_key" ON "RouteChangeRequest"(route_plan_id, client_request_id) WHERE client_request_id IS NOT NULL;
            CREATE INDEX "IX_RouteChangeRequest_created_at" ON "RouteChangeRequest"(created_at);
            CREATE INDEX "IX_RouteChangeRequest_backup_route_plan_id" ON "RouteChangeRequest"(backup_route_plan_id);
            CREATE INDEX "IX_RouteChangeRequest_requested_by_employee_id" ON "RouteChangeRequest"(requested_by_employee_id);
            CREATE INDEX "IX_RouteChangeRequest_decided_by_employee_id" ON "RouteChangeRequest"(decided_by_employee_id);
            CREATE INDEX "IX_RouteChangeRequest_incident_report_id" ON "RouteChangeRequest"(incident_report_id);
            ALTER TABLE "RoutePlanRoute" ADD COLUMN superseded_by_change_request_id uuid;
            ALTER TABLE "RoutePlanRoute" ADD CONSTRAINT "FK_RoutePlanRoute_RouteChangeRequest_superseded_by_change_request~"
                FOREIGN KEY(superseded_by_change_request_id) REFERENCES "RouteChangeRequest"(route_change_request_id) ON DELETE RESTRICT;
            CREATE INDEX "IX_RoutePlanRoute_superseded_by_change_request_id" ON "RoutePlanRoute"(superseded_by_change_request_id);
            """);
    }
}
