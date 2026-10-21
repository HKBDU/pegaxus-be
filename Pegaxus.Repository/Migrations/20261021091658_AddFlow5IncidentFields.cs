using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pegaxus.Repository.Enum;

#nullable disable

namespace Pegaxus.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddFlow5IncidentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:account_status", "ACTIVE,INACTIVE,SUSPENDED")
                .Annotation("Npgsql:Enum:account_type", "CUSTOMER,EMPLOYEE")
                .Annotation("Npgsql:Enum:approval_decision", "APPROVED,REJECTED")
                .Annotation("Npgsql:Enum:audit_visibility", "Public,Internal")
                .Annotation("Npgsql:Enum:authority_evidence_type", "Acceptance,Rejection,AdditionalInformation,Other")
                .Annotation("Npgsql:Enum:authority_submission_status", "DRAFT,SUBMITTED,PENDING,APPROVED,REJECTED,RETURNED")
                .Annotation("Npgsql:Enum:booking_history_type", "SUBMIT,APPROVE,REJECT,CANCEL")
                .Annotation("Npgsql:Enum:booking_status", "DRAFT,PENDING,APPROVED,REJECTED,CANCELLED")
                .Annotation("Npgsql:Enum:claim_resolution_type", "COMPENSATION,PARTIAL_COMPENSATION,REFUND,SERVICE_RECOVERY,NO_COMPENSATION,OTHER")
                .Annotation("Npgsql:Enum:claim_status", "PENDING,APPROVED,REJECTED,RESOLVED,CANCELLED")
                .Annotation("Npgsql:Enum:claim_type", "HORSE_INJURY,HORSE_HEALTH,LOSS,DAMAGE,DELAY,DOCUMENT_ISSUE,SERVICE_ISSUE,OTHER")
                .Annotation("Npgsql:Enum:compliance_document_status", "PROVIDED,PENDING,APPROVED,REJECTED,EXPIRED")
                .Annotation("Npgsql:Enum:compliance_dossier_status", "DRAFT,IN_PREPARATION,READY_FOR_SUBMISSION,SUBMITTED,PENDING,APPROVED,REJECTED,CLOSED")
                .Annotation("Npgsql:Enum:compliance_requirement_category", "Quarantine,Customs,Transport,HorseHealth,HorseIdentity,Other")
                .Annotation("Npgsql:Enum:compliance_requirement_status", "ACTIVE,INACTIVE")
                .Annotation("Npgsql:Enum:customs_clearance_proof_status", "PendingVerification,Cleared,Held,Rejected")
                .Annotation("Npgsql:Enum:customs_lane", "GreenLane,OrangeLane,RedLane")
                .Annotation("Npgsql:Enum:document_request_status", "OPEN,FULFILLED,CANCELLED")
                .Annotation("Npgsql:Enum:document_request_type", "MISSING_DOCUMENT,REVISION,REUPLOAD,ADDITIONAL_INFORMATION")
                .Annotation("Npgsql:Enum:document_review_result", "APPROVED,REJECTED,REVISION_REQUIRED,REVOKED")
                .Annotation("Npgsql:Enum:dossier_requirement_status", "PENDING,REQUESTED,PROVIDED,SATISFIED,REJECTED,WAIVED")
                .Annotation("Npgsql:Enum:dossier_template_status", "DRAFT,ACTIVE,INACTIVE")
                .Annotation("Npgsql:Enum:dossier_type", "MAIN,EXTRA")
                .Annotation("Npgsql:Enum:emergency_cost_category", "VETERINARY,MEDICATION,VEHICLE_REPAIR,TOWING,REPLACEMENT_VEHICLE,EMERGENCY_STABLE,ROUTE_CHANGE,ACCOMMODATION,OTHER")
                .Annotation("Npgsql:Enum:emergency_cost_status", "PENDING,APPROVED,REJECTED,CANCELLED")
                .Annotation("Npgsql:Enum:emergency_receipt_status", "NOT_REQUIRED,PENDING,SUBMITTED,APPROVED,REJECTED")
                .Annotation("Npgsql:Enum:employee_role", "LOGISTICS_MANAGER,TRANSPORT_SPECIALIST,FLEET_ROUTE_COORDINATOR,DRIVER,ESCORT")
                .Annotation("Npgsql:Enum:handover_party_type", "CUSTOMER,EMPLOYEE,PROVIDER")
                .Annotation("Npgsql:Enum:handover_status", "PLANNED,IN_PROGRESS,COMPLETED,CANCELLED")
                .Annotation("Npgsql:Enum:handover_type", "PICKUP,TRANSFER,BORDER_HANDOVER,CARRIER_HANDOVER,DELIVERY,OTHER")
                .Annotation("Npgsql:Enum:horse_health_log_type", "PRE_TRIP_CHECK,ROUTINE_CHECK,BORDER_CHECK,REST_STOP_CHECK,INCIDENT_FOLLOWUP,POST_TRIP_CHECK,OTHER")
                .Annotation("Npgsql:Enum:horse_health_status", "NORMAL,OBSERVATION_REQUIRED,AT_RISK,CRITICAL")
                .Annotation("Npgsql:Enum:horse_incident_type", "INJURY,DEHYDRATION,RESPIRATORY_ISSUE,FEVER,STRESS,COLIC,FATIGUE,OTHER")
                .Annotation("Npgsql:Enum:horse_sex", "MALE,FEMALE,GELDING")
                .Annotation("Npgsql:Enum:hydration_status", "NORMAL,MILD_DEHYDRATION,MODERATE_DEHYDRATION,SEVERE_DEHYDRATION")
                .Annotation("Npgsql:Enum:incident_category", "VEHICLE,HORSE_HEALTH,BOTH,ROUTE_CHANGE")
                .Annotation("Npgsql:Enum:incident_severity", "LOW,MEDIUM,HIGH,CRITICAL")
                .Annotation("Npgsql:Enum:incident_status", "REPORTED,PENDING,RESOLVED,CANCELLED")
                .Annotation("Npgsql:Enum:invoice_history_type", "CREATE,ISSUE,RECORD_PAYMENT,MARK_OVERDUE,CANCEL")
                .Annotation("Npgsql:Enum:invoice_item_type", "TRANSPORT_FEE,HORSE_SERVICE_FEE,HANDLING_FEE,COMPLIANCE_FEE,EMERGENCY_COST,OTHER")
                .Annotation("Npgsql:Enum:invoice_status", "DRAFT,ISSUED,PARTIALLY_PAID,PAID,OVERDUE,CANCELLED")
                .Annotation("Npgsql:Enum:invoice_type", "QUOTATION,DEPOSIT,FINAL,SUPPLEMENTARY,CREDIT_NOTE")
                .Annotation("Npgsql:Enum:location_type", "CITY,BORDER_GATE,AIRPORT,SEAPORT,STABLE,REST_STOP,OTHER")
                .Annotation("Npgsql:Enum:notification_priority", "LOW,NORMAL,HIGH,URGENT")
                .Annotation("Npgsql:Enum:notification_type", "BOOKING,TRANSPORT,ROUTE,HANDOVER,HORSE_HEALTH,INCIDENT,EMERGENCY_COST,PAYMENT,CLAIM,COMPLIANCE,DOCUMENT,SYSTEM")
                .Annotation("Npgsql:Enum:payment_method", "CASH,BANK_TRANSFER,OTHER")
                .Annotation("Npgsql:Enum:provider_status", "ACTIVE,INACTIVE,SUSPENDED")
                .Annotation("Npgsql:Enum:quarantine_proof_status", "PendingVerification,Passed,PassedWithConditions,Holdback,Failed,Rejected")
                .Annotation("Npgsql:Enum:receipt_status", "PENDING,CONFIRMED,FAILED,REFUNDED")
                .Annotation("Npgsql:Enum:recipient_status", "PENDING,DELIVERED,READ,FAILED")
                .Annotation("Npgsql:Enum:route_plan_status", "DRAFT,CONFIRMED,ACTIVE,COMPLETED,CANCELLED,PENDING_LEGAL_REVIEW,PENDING_MANAGER_APPROVAL,REJECTED_BY_MANAGER,APPROVED")
                .Annotation("Npgsql:Enum:route_segment_status", "PLANNED,IN_TRANSIT,COMPLETED")
                .Annotation("Npgsql:Enum:route_type", "PRIMARY,BACKUP,RETURN")
                .Annotation("Npgsql:Enum:transport_mode", "ROAD,SEA,AIR")
                .Annotation("Npgsql:Enum:transport_plan_status", "DRAFT,PENDING,APPROVED,IN_PROGRESS,COMPLETED,CANCELLED")
                .Annotation("Npgsql:Enum:transport_status", "CREATED,PLANNING,READY,IN_TRANSIT,COMPLETED,CANCELLED")
                .Annotation("Npgsql:Enum:vehicle_incident_type", "BREAKDOWN,ACCIDENT,TIRE_DAMAGE,ENGINE_FAILURE,TEMPERATURE_CONTROL_FAILURE,EQUIPMENT_FAILURE,OTHER")
                .Annotation("Npgsql:Enum:vehicle_status", "AVAILABLE,ASSIGNED,IN_USE,MAINTENANCE,INACTIVE,SUSPENDED,OUT_OF_SERVICE")
                .OldAnnotation("Npgsql:Enum:account_status", "ACTIVE,INACTIVE,SUSPENDED")
                .OldAnnotation("Npgsql:Enum:account_type", "CUSTOMER,EMPLOYEE")
                .OldAnnotation("Npgsql:Enum:approval_decision", "APPROVED,REJECTED")
                .OldAnnotation("Npgsql:Enum:audit_visibility", "Public,Internal")
                .OldAnnotation("Npgsql:Enum:authority_evidence_type", "Acceptance,Rejection,AdditionalInformation,Other")
                .OldAnnotation("Npgsql:Enum:authority_submission_status", "DRAFT,SUBMITTED,PENDING,APPROVED,REJECTED,RETURNED")
                .OldAnnotation("Npgsql:Enum:booking_history_type", "SUBMIT,APPROVE,REJECT,CANCEL")
                .OldAnnotation("Npgsql:Enum:booking_status", "DRAFT,PENDING,APPROVED,REJECTED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:claim_resolution_type", "COMPENSATION,PARTIAL_COMPENSATION,REFUND,SERVICE_RECOVERY,NO_COMPENSATION,OTHER")
                .OldAnnotation("Npgsql:Enum:claim_status", "PENDING,APPROVED,REJECTED,RESOLVED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:claim_type", "HORSE_INJURY,HORSE_HEALTH,LOSS,DAMAGE,DELAY,DOCUMENT_ISSUE,SERVICE_ISSUE,OTHER")
                .OldAnnotation("Npgsql:Enum:compliance_document_status", "PROVIDED,PENDING,APPROVED,REJECTED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:compliance_dossier_status", "DRAFT,IN_PREPARATION,READY_FOR_SUBMISSION,SUBMITTED,PENDING,APPROVED,REJECTED,CLOSED")
                .OldAnnotation("Npgsql:Enum:compliance_requirement_category", "Quarantine,Customs,Transport,HorseHealth,HorseIdentity,Other")
                .OldAnnotation("Npgsql:Enum:compliance_requirement_status", "ACTIVE,INACTIVE")
                .OldAnnotation("Npgsql:Enum:customs_clearance_proof_status", "PendingVerification,Cleared,Held,Rejected")
                .OldAnnotation("Npgsql:Enum:customs_lane", "GreenLane,OrangeLane,RedLane")
                .OldAnnotation("Npgsql:Enum:document_request_status", "OPEN,FULFILLED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:document_request_type", "MISSING_DOCUMENT,REVISION,REUPLOAD,ADDITIONAL_INFORMATION")
                .OldAnnotation("Npgsql:Enum:document_review_result", "APPROVED,REJECTED,REVISION_REQUIRED,REVOKED")
                .OldAnnotation("Npgsql:Enum:dossier_requirement_status", "PENDING,REQUESTED,PROVIDED,SATISFIED,REJECTED,WAIVED")
                .OldAnnotation("Npgsql:Enum:dossier_template_status", "DRAFT,ACTIVE,INACTIVE")
                .OldAnnotation("Npgsql:Enum:dossier_type", "MAIN,EXTRA")
                .OldAnnotation("Npgsql:Enum:emergency_cost_category", "VETERINARY,MEDICATION,VEHICLE_REPAIR,TOWING,REPLACEMENT_VEHICLE,EMERGENCY_STABLE,ROUTE_CHANGE,ACCOMMODATION,OTHER")
                .OldAnnotation("Npgsql:Enum:emergency_cost_status", "PENDING,APPROVED,REJECTED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:employee_role", "LOGISTICS_MANAGER,TRANSPORT_SPECIALIST,FLEET_ROUTE_COORDINATOR,DRIVER,ESCORT")
                .OldAnnotation("Npgsql:Enum:handover_party_type", "CUSTOMER,EMPLOYEE,PROVIDER")
                .OldAnnotation("Npgsql:Enum:handover_status", "PLANNED,IN_PROGRESS,COMPLETED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:handover_type", "PICKUP,TRANSFER,BORDER_HANDOVER,CARRIER_HANDOVER,DELIVERY,OTHER")
                .OldAnnotation("Npgsql:Enum:horse_health_log_type", "PRE_TRIP_CHECK,ROUTINE_CHECK,BORDER_CHECK,REST_STOP_CHECK,INCIDENT_FOLLOWUP,POST_TRIP_CHECK,OTHER")
                .OldAnnotation("Npgsql:Enum:horse_health_status", "NORMAL,OBSERVATION_REQUIRED,AT_RISK,CRITICAL")
                .OldAnnotation("Npgsql:Enum:horse_incident_type", "INJURY,DEHYDRATION,RESPIRATORY_ISSUE,FEVER,STRESS,COLIC,FATIGUE,OTHER")
                .OldAnnotation("Npgsql:Enum:horse_sex", "MALE,FEMALE,GELDING")
                .OldAnnotation("Npgsql:Enum:hydration_status", "NORMAL,MILD_DEHYDRATION,MODERATE_DEHYDRATION,SEVERE_DEHYDRATION")
                .OldAnnotation("Npgsql:Enum:incident_category", "VEHICLE,HORSE_HEALTH,BOTH,ROUTE_CHANGE")
                .OldAnnotation("Npgsql:Enum:incident_severity", "LOW,MEDIUM,HIGH,CRITICAL")
                .OldAnnotation("Npgsql:Enum:incident_status", "REPORTED,PENDING,RESOLVED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:invoice_history_type", "CREATE,ISSUE,RECORD_PAYMENT,MARK_OVERDUE,CANCEL")
                .OldAnnotation("Npgsql:Enum:invoice_item_type", "TRANSPORT_FEE,HORSE_SERVICE_FEE,HANDLING_FEE,COMPLIANCE_FEE,EMERGENCY_COST,OTHER")
                .OldAnnotation("Npgsql:Enum:invoice_status", "DRAFT,ISSUED,PARTIALLY_PAID,PAID,OVERDUE,CANCELLED")
                .OldAnnotation("Npgsql:Enum:invoice_type", "QUOTATION,DEPOSIT,FINAL,SUPPLEMENTARY,CREDIT_NOTE")
                .OldAnnotation("Npgsql:Enum:location_type", "CITY,BORDER_GATE,AIRPORT,SEAPORT,STABLE,REST_STOP,OTHER")
                .OldAnnotation("Npgsql:Enum:notification_priority", "LOW,NORMAL,HIGH,URGENT")
                .OldAnnotation("Npgsql:Enum:notification_type", "BOOKING,TRANSPORT,ROUTE,HANDOVER,HORSE_HEALTH,INCIDENT,EMERGENCY_COST,PAYMENT,CLAIM,COMPLIANCE,DOCUMENT,SYSTEM")
                .OldAnnotation("Npgsql:Enum:payment_method", "CASH,BANK_TRANSFER,OTHER")
                .OldAnnotation("Npgsql:Enum:provider_status", "ACTIVE,INACTIVE,SUSPENDED")
                .OldAnnotation("Npgsql:Enum:quarantine_proof_status", "PendingVerification,Passed,PassedWithConditions,Holdback,Failed,Rejected")
                .OldAnnotation("Npgsql:Enum:receipt_status", "PENDING,CONFIRMED,FAILED,REFUNDED")
                .OldAnnotation("Npgsql:Enum:recipient_status", "PENDING,DELIVERED,READ,FAILED")
                .OldAnnotation("Npgsql:Enum:route_plan_status", "DRAFT,CONFIRMED,ACTIVE,COMPLETED,CANCELLED,PENDING_LEGAL_REVIEW,PENDING_MANAGER_APPROVAL,REJECTED_BY_MANAGER,APPROVED")
                .OldAnnotation("Npgsql:Enum:route_segment_status", "PLANNED,IN_TRANSIT,COMPLETED")
                .OldAnnotation("Npgsql:Enum:route_type", "PRIMARY,BACKUP,RETURN")
                .OldAnnotation("Npgsql:Enum:transport_mode", "ROAD,SEA,AIR")
                .OldAnnotation("Npgsql:Enum:transport_plan_status", "DRAFT,PENDING,APPROVED,IN_PROGRESS,COMPLETED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:transport_status", "CREATED,PLANNING,READY,IN_TRANSIT,COMPLETED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:vehicle_incident_type", "BREAKDOWN,ACCIDENT,TIRE_DAMAGE,ENGINE_FAILURE,TEMPERATURE_CONTROL_FAILURE,EQUIPMENT_FAILURE,OTHER")
                .OldAnnotation("Npgsql:Enum:vehicle_status", "AVAILABLE,ASSIGNED,IN_USE,MAINTENANCE,INACTIVE,SUSPENDED,OUT_OF_SERVICE");

            migrationBuilder.AddColumn<string>(
                name: "cargo_condition_note",
                table: "VehicleIncidentDetail",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_safety_status",
                table: "VehicleIncidentDetail",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_repair_hours",
                table: "VehicleIncidentDetail",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "isolation_required",
                table: "HorseHealthIncidentDetail",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "invoice_required",
                table: "EmergencyCostRequest",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<EmergencyReceiptStatus>(
                name: "receipt_status",
                table: "EmergencyCostRequest",
                type: "emergency_receipt_status",
                nullable: false,
                defaultValueSql: "'NOT_REQUIRED'::emergency_receipt_status");

            migrationBuilder.AddColumn<Guid>(
                name: "requested_by_employee_id",
                table: "EmergencyCostRequest",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE "EmergencyCostRequest" AS ecr
                SET requested_by_employee_id = ir.employee_id
                FROM "IncidentReport" AS ir
                WHERE ecr.incident_report_id = ir.incident_report_id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "requested_by_employee_id",
                table: "EmergencyCostRequest",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "IncidentPhoto",
                columns: table => new
                {
                    incident_photo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_incident_detail_id = table.Column<Guid>(type: "uuid", nullable: true),
                    horse_health_incident_detail_id = table.Column<Guid>(type: "uuid", nullable: true),
                    emergency_cost_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("IncidentPhoto_pkey", x => x.incident_photo_id);
                    table.ForeignKey(
                        name: "IncidentPhoto_emergency_cost_request_id_fkey",
                        column: x => x.emergency_cost_request_id,
                        principalTable: "EmergencyCostRequest",
                        principalColumn: "emergency_cost_request_id");
                    table.ForeignKey(
                        name: "IncidentPhoto_horse_health_incident_detail_id_fkey",
                        column: x => x.horse_health_incident_detail_id,
                        principalTable: "HorseHealthIncidentDetail",
                        principalColumn: "horse_health_incident_detail_id");
                    table.ForeignKey(
                        name: "IncidentPhoto_incident_report_id_fkey",
                        column: x => x.incident_report_id,
                        principalTable: "IncidentReport",
                        principalColumn: "incident_report_id");
                    table.ForeignKey(
                        name: "IncidentPhoto_vehicle_incident_detail_id_fkey",
                        column: x => x.vehicle_incident_detail_id,
                        principalTable: "VehicleIncidentDetail",
                        principalColumn: "vehicle_incident_detail_id");
                });

            migrationBuilder.CreateIndex(
                name: "EmergencyCostRequest_receipt_status_idx",
                table: "EmergencyCostRequest",
                column: "receipt_status");

            migrationBuilder.CreateIndex(
                name: "EmergencyCostRequest_requested_by_employee_id_idx",
                table: "EmergencyCostRequest",
                column: "requested_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "IncidentPhoto_emergency_cost_request_id_idx",
                table: "IncidentPhoto",
                column: "emergency_cost_request_id");

            migrationBuilder.CreateIndex(
                name: "IncidentPhoto_horse_health_incident_detail_id_idx",
                table: "IncidentPhoto",
                column: "horse_health_incident_detail_id");

            migrationBuilder.CreateIndex(
                name: "IncidentPhoto_incident_report_id_idx",
                table: "IncidentPhoto",
                column: "incident_report_id");

            migrationBuilder.CreateIndex(
                name: "IncidentPhoto_vehicle_incident_detail_id_idx",
                table: "IncidentPhoto",
                column: "vehicle_incident_detail_id");

            migrationBuilder.AddForeignKey(
                name: "EmergencyCostRequest_requested_by_employee_id_fkey",
                table: "EmergencyCostRequest",
                column: "requested_by_employee_id",
                principalTable: "Employee",
                principalColumn: "employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "EmergencyCostRequest_requested_by_employee_id_fkey",
                table: "EmergencyCostRequest");

            migrationBuilder.DropTable(
                name: "IncidentPhoto");

            migrationBuilder.DropIndex(
                name: "EmergencyCostRequest_receipt_status_idx",
                table: "EmergencyCostRequest");

            migrationBuilder.DropIndex(
                name: "EmergencyCostRequest_requested_by_employee_id_idx",
                table: "EmergencyCostRequest");

            migrationBuilder.DropColumn(
                name: "cargo_condition_note",
                table: "VehicleIncidentDetail");

            migrationBuilder.DropColumn(
                name: "driver_safety_status",
                table: "VehicleIncidentDetail");

            migrationBuilder.DropColumn(
                name: "estimated_repair_hours",
                table: "VehicleIncidentDetail");

            migrationBuilder.DropColumn(
                name: "isolation_required",
                table: "HorseHealthIncidentDetail");

            migrationBuilder.DropColumn(
                name: "invoice_required",
                table: "EmergencyCostRequest");

            migrationBuilder.DropColumn(
                name: "receipt_status",
                table: "EmergencyCostRequest");

            migrationBuilder.DropColumn(
                name: "requested_by_employee_id",
                table: "EmergencyCostRequest");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:account_status", "ACTIVE,INACTIVE,SUSPENDED")
                .Annotation("Npgsql:Enum:account_type", "CUSTOMER,EMPLOYEE")
                .Annotation("Npgsql:Enum:approval_decision", "APPROVED,REJECTED")
                .Annotation("Npgsql:Enum:audit_visibility", "Public,Internal")
                .Annotation("Npgsql:Enum:authority_evidence_type", "Acceptance,Rejection,AdditionalInformation,Other")
                .Annotation("Npgsql:Enum:authority_submission_status", "DRAFT,SUBMITTED,PENDING,APPROVED,REJECTED,RETURNED")
                .Annotation("Npgsql:Enum:booking_history_type", "SUBMIT,APPROVE,REJECT,CANCEL")
                .Annotation("Npgsql:Enum:booking_status", "DRAFT,PENDING,APPROVED,REJECTED,CANCELLED")
                .Annotation("Npgsql:Enum:claim_resolution_type", "COMPENSATION,PARTIAL_COMPENSATION,REFUND,SERVICE_RECOVERY,NO_COMPENSATION,OTHER")
                .Annotation("Npgsql:Enum:claim_status", "PENDING,APPROVED,REJECTED,RESOLVED,CANCELLED")
                .Annotation("Npgsql:Enum:claim_type", "HORSE_INJURY,HORSE_HEALTH,LOSS,DAMAGE,DELAY,DOCUMENT_ISSUE,SERVICE_ISSUE,OTHER")
                .Annotation("Npgsql:Enum:compliance_document_status", "PROVIDED,PENDING,APPROVED,REJECTED,EXPIRED")
                .Annotation("Npgsql:Enum:compliance_dossier_status", "DRAFT,IN_PREPARATION,READY_FOR_SUBMISSION,SUBMITTED,PENDING,APPROVED,REJECTED,CLOSED")
                .Annotation("Npgsql:Enum:compliance_requirement_category", "Quarantine,Customs,Transport,HorseHealth,HorseIdentity,Other")
                .Annotation("Npgsql:Enum:compliance_requirement_status", "ACTIVE,INACTIVE")
                .Annotation("Npgsql:Enum:customs_clearance_proof_status", "PendingVerification,Cleared,Held,Rejected")
                .Annotation("Npgsql:Enum:customs_lane", "GreenLane,OrangeLane,RedLane")
                .Annotation("Npgsql:Enum:document_request_status", "OPEN,FULFILLED,CANCELLED")
                .Annotation("Npgsql:Enum:document_request_type", "MISSING_DOCUMENT,REVISION,REUPLOAD,ADDITIONAL_INFORMATION")
                .Annotation("Npgsql:Enum:document_review_result", "APPROVED,REJECTED,REVISION_REQUIRED,REVOKED")
                .Annotation("Npgsql:Enum:dossier_requirement_status", "PENDING,REQUESTED,PROVIDED,SATISFIED,REJECTED,WAIVED")
                .Annotation("Npgsql:Enum:dossier_template_status", "DRAFT,ACTIVE,INACTIVE")
                .Annotation("Npgsql:Enum:dossier_type", "MAIN,EXTRA")
                .Annotation("Npgsql:Enum:emergency_cost_category", "VETERINARY,MEDICATION,VEHICLE_REPAIR,TOWING,REPLACEMENT_VEHICLE,EMERGENCY_STABLE,ROUTE_CHANGE,ACCOMMODATION,OTHER")
                .Annotation("Npgsql:Enum:emergency_cost_status", "PENDING,APPROVED,REJECTED,CANCELLED")
                .Annotation("Npgsql:Enum:employee_role", "LOGISTICS_MANAGER,TRANSPORT_SPECIALIST,FLEET_ROUTE_COORDINATOR,DRIVER,ESCORT")
                .Annotation("Npgsql:Enum:handover_party_type", "CUSTOMER,EMPLOYEE,PROVIDER")
                .Annotation("Npgsql:Enum:handover_status", "PLANNED,IN_PROGRESS,COMPLETED,CANCELLED")
                .Annotation("Npgsql:Enum:handover_type", "PICKUP,TRANSFER,BORDER_HANDOVER,CARRIER_HANDOVER,DELIVERY,OTHER")
                .Annotation("Npgsql:Enum:horse_health_log_type", "PRE_TRIP_CHECK,ROUTINE_CHECK,BORDER_CHECK,REST_STOP_CHECK,INCIDENT_FOLLOWUP,POST_TRIP_CHECK,OTHER")
                .Annotation("Npgsql:Enum:horse_health_status", "NORMAL,OBSERVATION_REQUIRED,AT_RISK,CRITICAL")
                .Annotation("Npgsql:Enum:horse_incident_type", "INJURY,DEHYDRATION,RESPIRATORY_ISSUE,FEVER,STRESS,COLIC,FATIGUE,OTHER")
                .Annotation("Npgsql:Enum:horse_sex", "MALE,FEMALE,GELDING")
                .Annotation("Npgsql:Enum:hydration_status", "NORMAL,MILD_DEHYDRATION,MODERATE_DEHYDRATION,SEVERE_DEHYDRATION")
                .Annotation("Npgsql:Enum:incident_category", "VEHICLE,HORSE_HEALTH,BOTH,ROUTE_CHANGE")
                .Annotation("Npgsql:Enum:incident_severity", "LOW,MEDIUM,HIGH,CRITICAL")
                .Annotation("Npgsql:Enum:incident_status", "REPORTED,PENDING,RESOLVED,CANCELLED")
                .Annotation("Npgsql:Enum:invoice_history_type", "CREATE,ISSUE,RECORD_PAYMENT,MARK_OVERDUE,CANCEL")
                .Annotation("Npgsql:Enum:invoice_item_type", "TRANSPORT_FEE,HORSE_SERVICE_FEE,HANDLING_FEE,COMPLIANCE_FEE,EMERGENCY_COST,OTHER")
                .Annotation("Npgsql:Enum:invoice_status", "DRAFT,ISSUED,PARTIALLY_PAID,PAID,OVERDUE,CANCELLED")
                .Annotation("Npgsql:Enum:invoice_type", "QUOTATION,DEPOSIT,FINAL,SUPPLEMENTARY,CREDIT_NOTE")
                .Annotation("Npgsql:Enum:location_type", "CITY,BORDER_GATE,AIRPORT,SEAPORT,STABLE,REST_STOP,OTHER")
                .Annotation("Npgsql:Enum:notification_priority", "LOW,NORMAL,HIGH,URGENT")
                .Annotation("Npgsql:Enum:notification_type", "BOOKING,TRANSPORT,ROUTE,HANDOVER,HORSE_HEALTH,INCIDENT,EMERGENCY_COST,PAYMENT,CLAIM,COMPLIANCE,DOCUMENT,SYSTEM")
                .Annotation("Npgsql:Enum:payment_method", "CASH,BANK_TRANSFER,OTHER")
                .Annotation("Npgsql:Enum:provider_status", "ACTIVE,INACTIVE,SUSPENDED")
                .Annotation("Npgsql:Enum:quarantine_proof_status", "PendingVerification,Passed,PassedWithConditions,Holdback,Failed,Rejected")
                .Annotation("Npgsql:Enum:receipt_status", "PENDING,CONFIRMED,FAILED,REFUNDED")
                .Annotation("Npgsql:Enum:recipient_status", "PENDING,DELIVERED,READ,FAILED")
                .Annotation("Npgsql:Enum:route_plan_status", "DRAFT,CONFIRMED,ACTIVE,COMPLETED,CANCELLED,PENDING_LEGAL_REVIEW,PENDING_MANAGER_APPROVAL,REJECTED_BY_MANAGER,APPROVED")
                .Annotation("Npgsql:Enum:route_segment_status", "PLANNED,IN_TRANSIT,COMPLETED")
                .Annotation("Npgsql:Enum:route_type", "PRIMARY,BACKUP,RETURN")
                .Annotation("Npgsql:Enum:transport_mode", "ROAD,SEA,AIR")
                .Annotation("Npgsql:Enum:transport_plan_status", "DRAFT,PENDING,APPROVED,IN_PROGRESS,COMPLETED,CANCELLED")
                .Annotation("Npgsql:Enum:transport_status", "CREATED,PLANNING,READY,IN_TRANSIT,COMPLETED,CANCELLED")
                .Annotation("Npgsql:Enum:vehicle_incident_type", "BREAKDOWN,ACCIDENT,TIRE_DAMAGE,ENGINE_FAILURE,TEMPERATURE_CONTROL_FAILURE,EQUIPMENT_FAILURE,OTHER")
                .Annotation("Npgsql:Enum:vehicle_status", "AVAILABLE,ASSIGNED,IN_USE,MAINTENANCE,INACTIVE,SUSPENDED,OUT_OF_SERVICE")
                .OldAnnotation("Npgsql:Enum:account_status", "ACTIVE,INACTIVE,SUSPENDED")
                .OldAnnotation("Npgsql:Enum:account_type", "CUSTOMER,EMPLOYEE")
                .OldAnnotation("Npgsql:Enum:approval_decision", "APPROVED,REJECTED")
                .OldAnnotation("Npgsql:Enum:audit_visibility", "Public,Internal")
                .OldAnnotation("Npgsql:Enum:authority_evidence_type", "Acceptance,Rejection,AdditionalInformation,Other")
                .OldAnnotation("Npgsql:Enum:authority_submission_status", "DRAFT,SUBMITTED,PENDING,APPROVED,REJECTED,RETURNED")
                .OldAnnotation("Npgsql:Enum:booking_history_type", "SUBMIT,APPROVE,REJECT,CANCEL")
                .OldAnnotation("Npgsql:Enum:booking_status", "DRAFT,PENDING,APPROVED,REJECTED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:claim_resolution_type", "COMPENSATION,PARTIAL_COMPENSATION,REFUND,SERVICE_RECOVERY,NO_COMPENSATION,OTHER")
                .OldAnnotation("Npgsql:Enum:claim_status", "PENDING,APPROVED,REJECTED,RESOLVED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:claim_type", "HORSE_INJURY,HORSE_HEALTH,LOSS,DAMAGE,DELAY,DOCUMENT_ISSUE,SERVICE_ISSUE,OTHER")
                .OldAnnotation("Npgsql:Enum:compliance_document_status", "PROVIDED,PENDING,APPROVED,REJECTED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:compliance_dossier_status", "DRAFT,IN_PREPARATION,READY_FOR_SUBMISSION,SUBMITTED,PENDING,APPROVED,REJECTED,CLOSED")
                .OldAnnotation("Npgsql:Enum:compliance_requirement_category", "Quarantine,Customs,Transport,HorseHealth,HorseIdentity,Other")
                .OldAnnotation("Npgsql:Enum:compliance_requirement_status", "ACTIVE,INACTIVE")
                .OldAnnotation("Npgsql:Enum:customs_clearance_proof_status", "PendingVerification,Cleared,Held,Rejected")
                .OldAnnotation("Npgsql:Enum:customs_lane", "GreenLane,OrangeLane,RedLane")
                .OldAnnotation("Npgsql:Enum:document_request_status", "OPEN,FULFILLED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:document_request_type", "MISSING_DOCUMENT,REVISION,REUPLOAD,ADDITIONAL_INFORMATION")
                .OldAnnotation("Npgsql:Enum:document_review_result", "APPROVED,REJECTED,REVISION_REQUIRED,REVOKED")
                .OldAnnotation("Npgsql:Enum:dossier_requirement_status", "PENDING,REQUESTED,PROVIDED,SATISFIED,REJECTED,WAIVED")
                .OldAnnotation("Npgsql:Enum:dossier_template_status", "DRAFT,ACTIVE,INACTIVE")
                .OldAnnotation("Npgsql:Enum:dossier_type", "MAIN,EXTRA")
                .OldAnnotation("Npgsql:Enum:emergency_cost_category", "VETERINARY,MEDICATION,VEHICLE_REPAIR,TOWING,REPLACEMENT_VEHICLE,EMERGENCY_STABLE,ROUTE_CHANGE,ACCOMMODATION,OTHER")
                .OldAnnotation("Npgsql:Enum:emergency_cost_status", "PENDING,APPROVED,REJECTED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:emergency_receipt_status", "NOT_REQUIRED,PENDING,SUBMITTED,APPROVED,REJECTED")
                .OldAnnotation("Npgsql:Enum:employee_role", "LOGISTICS_MANAGER,TRANSPORT_SPECIALIST,FLEET_ROUTE_COORDINATOR,DRIVER,ESCORT")
                .OldAnnotation("Npgsql:Enum:handover_party_type", "CUSTOMER,EMPLOYEE,PROVIDER")
                .OldAnnotation("Npgsql:Enum:handover_status", "PLANNED,IN_PROGRESS,COMPLETED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:handover_type", "PICKUP,TRANSFER,BORDER_HANDOVER,CARRIER_HANDOVER,DELIVERY,OTHER")
                .OldAnnotation("Npgsql:Enum:horse_health_log_type", "PRE_TRIP_CHECK,ROUTINE_CHECK,BORDER_CHECK,REST_STOP_CHECK,INCIDENT_FOLLOWUP,POST_TRIP_CHECK,OTHER")
                .OldAnnotation("Npgsql:Enum:horse_health_status", "NORMAL,OBSERVATION_REQUIRED,AT_RISK,CRITICAL")
                .OldAnnotation("Npgsql:Enum:horse_incident_type", "INJURY,DEHYDRATION,RESPIRATORY_ISSUE,FEVER,STRESS,COLIC,FATIGUE,OTHER")
                .OldAnnotation("Npgsql:Enum:horse_sex", "MALE,FEMALE,GELDING")
                .OldAnnotation("Npgsql:Enum:hydration_status", "NORMAL,MILD_DEHYDRATION,MODERATE_DEHYDRATION,SEVERE_DEHYDRATION")
                .OldAnnotation("Npgsql:Enum:incident_category", "VEHICLE,HORSE_HEALTH,BOTH,ROUTE_CHANGE")
                .OldAnnotation("Npgsql:Enum:incident_severity", "LOW,MEDIUM,HIGH,CRITICAL")
                .OldAnnotation("Npgsql:Enum:incident_status", "REPORTED,PENDING,RESOLVED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:invoice_history_type", "CREATE,ISSUE,RECORD_PAYMENT,MARK_OVERDUE,CANCEL")
                .OldAnnotation("Npgsql:Enum:invoice_item_type", "TRANSPORT_FEE,HORSE_SERVICE_FEE,HANDLING_FEE,COMPLIANCE_FEE,EMERGENCY_COST,OTHER")
                .OldAnnotation("Npgsql:Enum:invoice_status", "DRAFT,ISSUED,PARTIALLY_PAID,PAID,OVERDUE,CANCELLED")
                .OldAnnotation("Npgsql:Enum:invoice_type", "QUOTATION,DEPOSIT,FINAL,SUPPLEMENTARY,CREDIT_NOTE")
                .OldAnnotation("Npgsql:Enum:location_type", "CITY,BORDER_GATE,AIRPORT,SEAPORT,STABLE,REST_STOP,OTHER")
                .OldAnnotation("Npgsql:Enum:notification_priority", "LOW,NORMAL,HIGH,URGENT")
                .OldAnnotation("Npgsql:Enum:notification_type", "BOOKING,TRANSPORT,ROUTE,HANDOVER,HORSE_HEALTH,INCIDENT,EMERGENCY_COST,PAYMENT,CLAIM,COMPLIANCE,DOCUMENT,SYSTEM")
                .OldAnnotation("Npgsql:Enum:payment_method", "CASH,BANK_TRANSFER,OTHER")
                .OldAnnotation("Npgsql:Enum:provider_status", "ACTIVE,INACTIVE,SUSPENDED")
                .OldAnnotation("Npgsql:Enum:quarantine_proof_status", "PendingVerification,Passed,PassedWithConditions,Holdback,Failed,Rejected")
                .OldAnnotation("Npgsql:Enum:receipt_status", "PENDING,CONFIRMED,FAILED,REFUNDED")
                .OldAnnotation("Npgsql:Enum:recipient_status", "PENDING,DELIVERED,READ,FAILED")
                .OldAnnotation("Npgsql:Enum:route_plan_status", "DRAFT,CONFIRMED,ACTIVE,COMPLETED,CANCELLED,PENDING_LEGAL_REVIEW,PENDING_MANAGER_APPROVAL,REJECTED_BY_MANAGER,APPROVED")
                .OldAnnotation("Npgsql:Enum:route_segment_status", "PLANNED,IN_TRANSIT,COMPLETED")
                .OldAnnotation("Npgsql:Enum:route_type", "PRIMARY,BACKUP,RETURN")
                .OldAnnotation("Npgsql:Enum:transport_mode", "ROAD,SEA,AIR")
                .OldAnnotation("Npgsql:Enum:transport_plan_status", "DRAFT,PENDING,APPROVED,IN_PROGRESS,COMPLETED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:transport_status", "CREATED,PLANNING,READY,IN_TRANSIT,COMPLETED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:vehicle_incident_type", "BREAKDOWN,ACCIDENT,TIRE_DAMAGE,ENGINE_FAILURE,TEMPERATURE_CONTROL_FAILURE,EQUIPMENT_FAILURE,OTHER")
                .OldAnnotation("Npgsql:Enum:vehicle_status", "AVAILABLE,ASSIGNED,IN_USE,MAINTENANCE,INACTIVE,SUSPENDED,OUT_OF_SERVICE");
        }
    }
}
