using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pegaxus.Repository.Enum;

#nullable disable

namespace Pegaxus.Repository.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:account_status", "ACTIVE,INACTIVE,SUSPENDED")
                .Annotation("Npgsql:Enum:account_type", "CUSTOMER,EMPLOYEE")
                .Annotation("Npgsql:Enum:approval_decision", "APPROVED,REJECTED")
                .Annotation("Npgsql:Enum:authority_submission_status", "DRAFT,SUBMITTED,PENDING,APPROVED,REJECTED,RETURNED")
                .Annotation("Npgsql:Enum:booking_history_type", "SUBMIT,APPROVE,REJECT,CANCEL")
                .Annotation("Npgsql:Enum:booking_status", "PENDING,APPROVED,REJECTED,CANCELLED")
                .Annotation("Npgsql:Enum:claim_resolution_type", "COMPENSATION,PARTIAL_COMPENSATION,REFUND,SERVICE_RECOVERY,NO_COMPENSATION,OTHER")
                .Annotation("Npgsql:Enum:claim_status", "PENDING,APPROVED,REJECTED,RESOLVED,CANCELLED")
                .Annotation("Npgsql:Enum:claim_type", "HORSE_INJURY,HORSE_HEALTH,LOSS,DAMAGE,DELAY,DOCUMENT_ISSUE,SERVICE_ISSUE,OTHER")
                .Annotation("Npgsql:Enum:compliance_document_status", "PROVIDED,PENDING,APPROVED,REJECTED,EXPIRED")
                .Annotation("Npgsql:Enum:compliance_dossier_status", "DRAFT,IN_PREPARATION,READY_FOR_SUBMISSION,SUBMITTED,PENDING,APPROVED,REJECTED,CLOSED")
                .Annotation("Npgsql:Enum:compliance_requirement_status", "ACTIVE,INACTIVE")
                .Annotation("Npgsql:Enum:document_request_status", "OPEN,FULFILLED,CANCELLED")
                .Annotation("Npgsql:Enum:document_request_type", "MISSING_DOCUMENT,REVISION,REUPLOAD,ADDITIONAL_INFORMATION")
                .Annotation("Npgsql:Enum:document_review_result", "APPROVED,REJECTED,REVISION_REQUIRED")
                .Annotation("Npgsql:Enum:dossier_requirement_status", "PENDING,REQUESTED,PROVIDED,SATISFIED,REJECTED,WAIVED")
                .Annotation("Npgsql:Enum:dossier_template_status", "DRAFT,ACTIVE,INACTIVE")
                .Annotation("Npgsql:Enum:emergency_cost_category", "VETERINARY,MEDICATION,VEHICLE_REPAIR,TOWING,REPLACEMENT_VEHICLE,EMERGENCY_STABLE,ROUTE_CHANGE,ACCOMMODATION,OTHER")
                .Annotation("Npgsql:Enum:emergency_cost_status", "PENDING,APPROVED,REJECTED,CANCELLED")
                .Annotation("Npgsql:Enum:employee_role", "LOGISTICS_MANAGER,TRANSPORT_SPECIALIST,FLEET_ROUTE_COORDINATOR,DRIVER,ESCORT")
                .Annotation("Npgsql:Enum:handover_party_type", "CUSTOMER,EMPLOYEE")
                .Annotation("Npgsql:Enum:handover_status", "PLANNED,IN_PROGRESS,COMPLETED,CANCELLED")
                .Annotation("Npgsql:Enum:handover_type", "PICKUP,TRANSFER,BORDER_HANDOVER,CARRIER_HANDOVER,DELIVERY,OTHER")
                .Annotation("Npgsql:Enum:horse_health_log_type", "PRE_TRIP_CHECK,ROUTINE_CHECK,BORDER_CHECK,REST_STOP_CHECK,INCIDENT_FOLLOWUP,POST_TRIP_CHECK,OTHER")
                .Annotation("Npgsql:Enum:horse_health_status", "NORMAL,OBSERVATION_REQUIRED,AT_RISK,CRITICAL")
                .Annotation("Npgsql:Enum:horse_incident_type", "INJURY,DEHYDRATION,RESPIRATORY_ISSUE,FEVER,STRESS,COLIC,FATIGUE,OTHER")
                .Annotation("Npgsql:Enum:horse_sex", "MALE,FEMALE,GELDING")
                .Annotation("Npgsql:Enum:hydration_status", "NORMAL,MILD_DEHYDRATION,MODERATE_DEHYDRATION,SEVERE_DEHYDRATION")
                .Annotation("Npgsql:Enum:incident_category", "VEHICLE,HORSE_HEALTH,BOTH")
                .Annotation("Npgsql:Enum:incident_severity", "LOW,MEDIUM,HIGH,CRITICAL")
                .Annotation("Npgsql:Enum:incident_status", "REPORTED,PENDING,RESOLVED,CANCELLED")
                .Annotation("Npgsql:Enum:invoice_history_type", "CREATE,ISSUE,RECORD_PAYMENT,MARK_OVERDUE,CANCEL")
                .Annotation("Npgsql:Enum:invoice_item_type", "TRANSPORT_FEE,HORSE_SERVICE_FEE,HANDLING_FEE,COMPLIANCE_FEE,EMERGENCY_COST,OTHER")
                .Annotation("Npgsql:Enum:invoice_status", "DRAFT,ISSUED,PARTIALLY_PAID,PAID,OVERDUE,CANCELLED")
                .Annotation("Npgsql:Enum:location_type", "CITY,BORDER_GATE,AIRPORT,SEAPORT,STABLE,REST_STOP,OTHER")
                .Annotation("Npgsql:Enum:notification_priority", "LOW,NORMAL,HIGH,URGENT")
                .Annotation("Npgsql:Enum:notification_type", "BOOKING,TRANSPORT,ROUTE,HANDOVER,HORSE_HEALTH,INCIDENT,EMERGENCY_COST,PAYMENT,CLAIM,COMPLIANCE,DOCUMENT,SYSTEM")
                .Annotation("Npgsql:Enum:payment_method", "CASH,BANK_TRANSFER,OTHER")
                .Annotation("Npgsql:Enum:provider_status", "ACTIVE,INACTIVE,SUSPENDED")
                .Annotation("Npgsql:Enum:receipt_status", "PENDING,CONFIRMED,FAILED,REFUNDED")
                .Annotation("Npgsql:Enum:recipient_status", "PENDING,DELIVERED,READ,FAILED")
                .Annotation("Npgsql:Enum:route_plan_status", "DRAFT,CONFIRMED,ACTIVE,COMPLETED,CANCELLED")
                .Annotation("Npgsql:Enum:route_type", "PRIMARY,BACKUP")
                .Annotation("Npgsql:Enum:transport_mode", "ROAD,SEA,AIR")
                .Annotation("Npgsql:Enum:transport_plan_status", "DRAFT,PENDING,APPROVED,IN_PROGRESS,COMPLETED,CANCELLED")
                .Annotation("Npgsql:Enum:transport_status", "CREATED,PLANNING,READY,IN_TRANSIT,COMPLETED,CANCELLED")
                .Annotation("Npgsql:Enum:vehicle_incident_type", "BREAKDOWN,ACCIDENT,TIRE_DAMAGE,ENGINE_FAILURE,TEMPERATURE_CONTROL_FAILURE,EQUIPMENT_FAILURE,OTHER")
                .Annotation("Npgsql:Enum:vehicle_status", "AVAILABLE,ASSIGNED,IN_USE,MAINTENANCE,INACTIVE");

            migrationBuilder.CreateTable(
                name: "Account",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    email_verified_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    avatar_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    account_type = table.Column<AccountType>(type: "account_type", nullable: false),
                    status = table.Column<AccountStatus>(type: "account_status", nullable: false, defaultValueSql: "'ACTIVE'::account_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("Account_pkey", x => x.account_id);
                    table.ForeignKey(
                        name: "Account_deleted_by_account_id_fkey",
                        column: x => x.deleted_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                });

            migrationBuilder.CreateTable(
                name: "ComplianceRequirement",
                columns: table => new
                {
                    compliance_requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    requirement_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    issuing_authority = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    validity_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<ComplianceRequirementStatus>(type: "compliance_requirement_status", nullable: false, defaultValueSql: "'ACTIVE'::compliance_requirement_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("ComplianceRequirement_pkey", x => x.compliance_requirement_id);
                    table.ForeignKey(
                        name: "ComplianceRequirement_deleted_by_account_id_fkey",
                        column: x => x.deleted_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                },
                comment: "ComplianceRequirement: Danh mục master các loại yêu cầu pháp lý hoặc loại giấy tờ mà hệ thống biết có thể cần trong hồ sơ.");

            migrationBuilder.CreateTable(
                name: "Customer",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    identity_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Customer_pkey", x => x.customer_id);
                    table.ForeignKey(
                        name: "Customer_account_id_fkey",
                        column: x => x.account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                    table.ForeignKey(
                        name: "Customer_deleted_by_account_id_fkey",
                        column: x => x.deleted_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                });

            migrationBuilder.CreateTable(
                name: "DossierTemplate",
                columns: table => new
                {
                    dossier_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    version_no = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<DossierTemplateStatus>(type: "dossier_template_status", nullable: false, defaultValueSql: "'DRAFT'::dossier_template_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("DossierTemplate_pkey", x => x.dossier_template_id);
                    table.ForeignKey(
                        name: "DossierTemplate_deleted_by_account_id_fkey",
                        column: x => x.deleted_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                },
                comment: "DossierTemplate: Bộ hồ sơ pháp lý mẫu cho một kịch bản vận chuyển. Template xác định thông thường cần những loại yêu cầu nào.");

            migrationBuilder.CreateTable(
                name: "Employee",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    role = table.Column<EmployeeRole>(type: "employee_role", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Employee_pkey", x => x.employee_id);
                    table.ForeignKey(
                        name: "Employee_account_id_fkey",
                        column: x => x.account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                    table.ForeignKey(
                        name: "Employee_deleted_by_account_id_fkey",
                        column: x => x.deleted_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                });

            migrationBuilder.CreateTable(
                name: "Location",
                columns: table => new
                {
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_type = table.Column<LocationType>(type: "location_type", nullable: true),
                    location_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state_province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    location_note = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Location_pkey", x => x.location_id);
                    table.ForeignKey(
                        name: "Location_deleted_by_account_id_fkey",
                        column: x => x.deleted_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                },
                comment: "Location: Địa điểm master trên bản đồ. Một địa điểm chỉ tạo một lần và có thể được nhiều Route tái sử dụng, ví dụ Thành phố Hồ Chí Minh.");

            migrationBuilder.CreateTable(
                name: "RefreshToken",
                columns: table => new
                {
                    refresh_token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revoked_by_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    device_info = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("RefreshToken_pkey", x => x.refresh_token_id);
                    table.ForeignKey(
                        name: "RefreshToken_account_id_fkey",
                        column: x => x.account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                    table.ForeignKey(
                        name: "RefreshToken_replaced_by_token_id_fkey",
                        column: x => x.replaced_by_token_id,
                        principalTable: "RefreshToken",
                        principalColumn: "refresh_token_id");
                },
                comment: "RefreshToken: Token dùng để cấp lại Access Token cho một Account. Một Account có thể có nhiều RefreshToken cho nhiều thiết bị/phiên đăng nhập. Database chỉ lưu token_hash, không lưu raw token.");

            migrationBuilder.CreateTable(
                name: "TransportProvider",
                columns: table => new
                {
                    transport_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<ProviderStatus>(type: "provider_status", nullable: false, defaultValueSql: "'ACTIVE'::provider_status"),
                    provider_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tax_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    supported_modes = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("TransportProvider_pkey", x => x.transport_provider_id);
                    table.ForeignKey(
                        name: "TransportProvider_deleted_by_account_id_fkey",
                        column: x => x.deleted_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                });

            migrationBuilder.CreateTable(
                name: "Vehicle",
                columns: table => new
                {
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transport_mode = table.Column<TransportMode>(type: "transport_mode", nullable: false),
                    status = table.Column<VehicleStatus>(type: "vehicle_status", nullable: false, defaultValueSql: "'AVAILABLE'::vehicle_status"),
                    vehicle_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    vehicle_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    license_plate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    vehicle_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    manufacturer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    capacity_horses = table.Column<int>(type: "integer", nullable: true),
                    registration_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    registration_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    insurance_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    insurance_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    special_equipment = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Vehicle_pkey", x => x.vehicle_id);
                    table.ForeignKey(
                        name: "Vehicle_deleted_by_account_id_fkey",
                        column: x => x.deleted_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                },
                comment: "Vehicle: Phương tiện vận chuyển master dùng chung cho ROAD, AIR hoặc SEA, ví dụ xe tải chở ngựa, máy bay hoặc tàu. Vehicle không thuộc cố định một TransportPlan; phương tiện được gán vào từng chặng RoutePlanRoute.");

            migrationBuilder.CreateTable(
                name: "Horse",
                columns: table => new
                {
                    horse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sex = table.Column<HorseSex>(type: "horse_sex", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    horse_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    passport_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    microchip_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    breed = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    color = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country_of_origin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    weight_kg = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    identification_notes = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Horse_pkey", x => x.horse_id);
                    table.ForeignKey(
                        name: "Horse_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "Customer",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "Horse_deleted_by_account_id_fkey",
                        column: x => x.deleted_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                });

            migrationBuilder.CreateTable(
                name: "DossierTemplateItem",
                columns: table => new
                {
                    dossier_template_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compliance_requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    instruction = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("DossierTemplateItem_pkey", x => x.dossier_template_item_id);
                    table.ForeignKey(
                        name: "DossierTemplateItem_compliance_requirement_id_fkey",
                        column: x => x.compliance_requirement_id,
                        principalTable: "ComplianceRequirement",
                        principalColumn: "compliance_requirement_id");
                    table.ForeignKey(
                        name: "DossierTemplateItem_dossier_template_id_fkey",
                        column: x => x.dossier_template_id,
                        principalTable: "DossierTemplate",
                        principalColumn: "dossier_template_id");
                },
                comment: "DossierTemplateItem: Một dòng yêu cầu nằm trong DossierTemplate, dùng để liên kết Template với ComplianceRequirement và đánh dấu yêu cầu bắt buộc hay không.");

            migrationBuilder.CreateTable(
                name: "Booking",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    origin_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    destination_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    destination_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    requested_departure_date = table.Column<DateOnly>(type: "date", nullable: false),
                    requested_arrival_date = table.Column<DateOnly>(type: "date", nullable: true),
                    special_requirements = table.Column<string>(type: "text", nullable: true),
                    customer_note = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<BookingStatus>(type: "booking_status", nullable: false, defaultValueSql: "'PENDING'::booking_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("Booking_pkey", x => x.booking_id);
                    table.ForeignKey(
                        name: "Booking_assigned_employee_id_fkey",
                        column: x => x.assigned_employee_id,
                        principalTable: "Employee",
                        principalColumn: "employee_id");
                    table.ForeignKey(
                        name: "Booking_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "Customer",
                        principalColumn: "customer_id");
                });

            migrationBuilder.CreateTable(
                name: "IncidentReport",
                columns: table => new
                {
                    incident_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<IncidentCategory>(type: "incident_category", nullable: false),
                    severity = table.Column<IncidentSeverity>(type: "incident_severity", nullable: false),
                    status = table.Column<IncidentStatus>(type: "incident_status", nullable: false, defaultValueSql: "'REPORTED'::incident_status"),
                    incident_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    reported_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    immediate_action = table.Column<string>(type: "text", nullable: true),
                    resolution_summary = table.Column<string>(type: "text", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("IncidentReport_pkey", x => x.incident_report_id);
                    table.ForeignKey(
                        name: "IncidentReport_employee_id_fkey",
                        column: x => x.employee_id,
                        principalTable: "Employee",
                        principalColumn: "employee_id");
                    table.ForeignKey(
                        name: "IncidentReport_location_id_fkey",
                        column: x => x.location_id,
                        principalTable: "Location",
                        principalColumn: "location_id");
                },
                comment: "IncidentReport: Báo cáo sự cố phát sinh trong quá trình vận chuyển. category gồm VEHICLE, HORSE_HEALTH hoặc BOTH.");

            migrationBuilder.CreateTable(
                name: "Route",
                columns: table => new
                {
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transport_mode = table.Column<TransportMode>(type: "transport_mode", nullable: false),
                    route_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    from_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    distance_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    estimated_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    route_note = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Route_pkey", x => x.route_id);
                    table.ForeignKey(
                        name: "Route_deleted_by_account_id_fkey",
                        column: x => x.deleted_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                    table.ForeignKey(
                        name: "Route_from_location_id_fkey",
                        column: x => x.from_location_id,
                        principalTable: "Location",
                        principalColumn: "location_id");
                    table.ForeignKey(
                        name: "Route_to_location_id_fkey",
                        column: x => x.to_location_id,
                        principalTable: "Location",
                        principalColumn: "location_id");
                },
                comment: "Route: Một tuyến đường có hướng nối từ một Location bắt đầu đến một Location kết thúc. Route có thể được tái sử dụng trong nhiều RoutePlan.");

            migrationBuilder.CreateTable(
                name: "BookingHorse",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    horse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    special_handling_notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("BookingHorse_pkey", x => new { x.booking_id, x.horse_id });
                    table.ForeignKey(
                        name: "BookingHorse_booking_id_fkey",
                        column: x => x.booking_id,
                        principalTable: "Booking",
                        principalColumn: "booking_id");
                    table.ForeignKey(
                        name: "BookingHorse_horse_id_fkey",
                        column: x => x.horse_id,
                        principalTable: "Horse",
                        principalColumn: "horse_id");
                });

            migrationBuilder.CreateTable(
                name: "BookingStatusHistory",
                columns: table => new
                {
                    booking_status_history_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    type = table.Column<BookingHistoryType>(type: "booking_history_type", nullable: false),
                    previous_status = table.Column<BookingStatus>(type: "booking_status", nullable: true),
                    new_status = table.Column<BookingStatus>(type: "booking_status", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("BookingStatusHistory_pkey", x => x.booking_status_history_id);
                    table.ForeignKey(
                        name: "BookingStatusHistory_booking_id_fkey",
                        column: x => x.booking_id,
                        principalTable: "Booking",
                        principalColumn: "booking_id");
                    table.ForeignKey(
                        name: "BookingStatusHistory_changed_by_account_id_fkey",
                        column: x => x.changed_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                },
                comment: "BookingStatusHistory: Lịch sử thay đổi trạng thái Booking. type cho biết hành động nghiệp vụ nào đã gây ra thay đổi status.");

            migrationBuilder.CreateTable(
                name: "Invoice",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<InvoiceStatus>(type: "invoice_status", nullable: false, defaultValueSql: "'DRAFT'::invoice_status"),
                    invoice_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    billing_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Invoice_pkey", x => x.invoice_id);
                    table.ForeignKey(
                        name: "Invoice_booking_id_fkey",
                        column: x => x.booking_id,
                        principalTable: "Booking",
                        principalColumn: "booking_id");
                    table.ForeignKey(
                        name: "Invoice_employee_id_fkey",
                        column: x => x.employee_id,
                        principalTable: "Employee",
                        principalColumn: "employee_id");
                });

            migrationBuilder.CreateTable(
                name: "Transport",
                columns: table => new
                {
                    transport_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<TransportStatus>(type: "transport_status", nullable: false, defaultValueSql: "'CREATED'::transport_status"),
                    transport_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_departure_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    actual_arrival_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Transport_pkey", x => x.transport_id);
                    table.ForeignKey(
                        name: "Transport_booking_id_fkey",
                        column: x => x.booking_id,
                        principalTable: "Booking",
                        principalColumn: "booking_id");
                });

            migrationBuilder.CreateTable(
                name: "EmergencyCostRequest",
                columns: table => new
                {
                    emergency_cost_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    requested_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    justification = table.Column<string>(type: "text", nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    approved_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    category = table.Column<EmergencyCostCategory>(type: "emergency_cost_category", nullable: false),
                    status = table.Column<EmergencyCostStatus>(type: "emergency_cost_status", nullable: false, defaultValueSql: "'PENDING'::emergency_cost_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("EmergencyCostRequest_pkey", x => x.emergency_cost_request_id);
                    table.ForeignKey(
                        name: "EmergencyCostRequest_incident_report_id_fkey",
                        column: x => x.incident_report_id,
                        principalTable: "IncidentReport",
                        principalColumn: "incident_report_id");
                });

            migrationBuilder.CreateTable(
                name: "HorseHealthIncidentDetail",
                columns: table => new
                {
                    horse_health_incident_detail_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_type = table.Column<HorseIncidentType>(type: "horse_incident_type", nullable: false),
                    incident_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    horse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condition_description = table.Column<string>(type: "text", nullable: false),
                    symptoms = table.Column<string>(type: "text", nullable: true),
                    body_temperature_celsius = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    heart_rate_bpm = table.Column<int>(type: "integer", nullable: true),
                    respiratory_rate_bpm = table.Column<int>(type: "integer", nullable: true),
                    first_aid_provided = table.Column<string>(type: "text", nullable: true),
                    veterinarian_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    veterinarian_contacted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    veterinarian_instruction = table.Column<string>(type: "text", nullable: true),
                    medication_given = table.Column<string>(type: "text", nullable: true),
                    horse_transportable = table.Column<bool>(type: "boolean", nullable: true),
                    outcome_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("HorseHealthIncidentDetail_pkey", x => x.horse_health_incident_detail_id);
                    table.ForeignKey(
                        name: "HorseHealthIncidentDetail_horse_id_fkey",
                        column: x => x.horse_id,
                        principalTable: "Horse",
                        principalColumn: "horse_id");
                    table.ForeignKey(
                        name: "HorseHealthIncidentDetail_incident_report_id_fkey",
                        column: x => x.incident_report_id,
                        principalTable: "IncidentReport",
                        principalColumn: "incident_report_id");
                });

            migrationBuilder.CreateTable(
                name: "VehicleIncidentDetail",
                columns: table => new
                {
                    vehicle_incident_detail_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_type = table.Column<VehicleIncidentType>(type: "vehicle_incident_type", nullable: false),
                    incident_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_condition = table.Column<string>(type: "text", nullable: true),
                    damage_description = table.Column<string>(type: "text", nullable: true),
                    vehicle_operable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    repair_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    towing_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    replacement_vehicle_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    estimated_repair_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    repair_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("VehicleIncidentDetail_pkey", x => x.vehicle_incident_detail_id);
                    table.ForeignKey(
                        name: "VehicleIncidentDetail_incident_report_id_fkey",
                        column: x => x.incident_report_id,
                        principalTable: "IncidentReport",
                        principalColumn: "incident_report_id");
                    table.ForeignKey(
                        name: "VehicleIncidentDetail_vehicle_id_fkey",
                        column: x => x.vehicle_id,
                        principalTable: "Vehicle",
                        principalColumn: "vehicle_id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceStatusHistory",
                columns: table => new
                {
                    invoice_status_history_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<InvoiceHistoryType>(type: "invoice_history_type", nullable: false),
                    previous_status = table.Column<InvoiceStatus>(type: "invoice_status", nullable: true),
                    new_status = table.Column<InvoiceStatus>(type: "invoice_status", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("InvoiceStatusHistory_pkey", x => x.invoice_status_history_id);
                    table.ForeignKey(
                        name: "InvoiceStatusHistory_changed_by_account_id_fkey",
                        column: x => x.changed_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                    table.ForeignKey(
                        name: "InvoiceStatusHistory_invoice_id_fkey",
                        column: x => x.invoice_id,
                        principalTable: "Invoice",
                        principalColumn: "invoice_id");
                },
                comment: "InvoiceStatusHistory: Lịch sử thay đổi trạng thái Invoice. type cho biết hành động nghiệp vụ gây ra thay đổi; changed_by_account_id có thể null nếu hệ thống tự động thực hiện, ví dụ MARK_OVERDUE.");

            migrationBuilder.CreateTable(
                name: "Receipt",
                columns: table => new
                {
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method = table.Column<PaymentMethod>(type: "payment_method", nullable: false),
                    status = table.Column<ReceiptStatus>(type: "receipt_status", nullable: false, defaultValueSql: "'PENDING'::receipt_status"),
                    receipt_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    transaction_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Receipt_pkey", x => x.receipt_id);
                    table.ForeignKey(
                        name: "Receipt_invoice_id_fkey",
                        column: x => x.invoice_id,
                        principalTable: "Invoice",
                        principalColumn: "invoice_id");
                });

            migrationBuilder.CreateTable(
                name: "ComplianceDossier",
                columns: table => new
                {
                    compliance_dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    transport_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ready_for_submission_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<ComplianceDossierStatus>(type: "compliance_dossier_status", nullable: false, defaultValueSql: "'DRAFT'::compliance_dossier_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("ComplianceDossier_pkey", x => x.compliance_dossier_id);
                    table.ForeignKey(
                        name: "ComplianceDossier_transport_id_fkey",
                        column: x => x.transport_id,
                        principalTable: "Transport",
                        principalColumn: "transport_id");
                },
                comment: "ComplianceDossier: Bộ hồ sơ pháp lý thực tế của một chuyến đi, tức của một Transport cụ thể.");

            migrationBuilder.CreateTable(
                name: "FinancialReport",
                columns: table => new
                {
                    financial_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    transport_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_invoiced_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_emergency_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    outstanding_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    summary_note = table.Column<string>(type: "text", nullable: true),
                    generated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("FinancialReport_pkey", x => x.financial_report_id);
                    table.ForeignKey(
                        name: "FinancialReport_transport_id_fkey",
                        column: x => x.transport_id,
                        principalTable: "Transport",
                        principalColumn: "transport_id");
                },
                comment: "FinancialReport: Báo cáo tài chính tổng hợp theo một Transport, được hệ thống sinh từ dữ liệu Invoice, InvoiceItem, Receipt và chi phí liên quan; không có trạng thái lưu nháp.");

            migrationBuilder.CreateTable(
                name: "HorseHealthLog",
                columns: table => new
                {
                    horse_health_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    log_type = table.Column<HorseHealthLogType>(type: "horse_health_log_type", nullable: false),
                    health_status = table.Column<HorseHealthStatus>(type: "horse_health_status", nullable: false),
                    hydration_status = table.Column<HydrationStatus>(type: "hydration_status", nullable: true),
                    transport_id = table.Column<Guid>(type: "uuid", nullable: false),
                    horse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    temperature_celsius = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    heart_rate_bpm = table.Column<int>(type: "integer", nullable: true),
                    respiratory_rate_bpm = table.Column<int>(type: "integer", nullable: true),
                    behavior_note = table.Column<string>(type: "text", nullable: true),
                    symptom_note = table.Column<string>(type: "text", nullable: true),
                    injury_note = table.Column<string>(type: "text", nullable: true),
                    feeding_note = table.Column<string>(type: "text", nullable: true),
                    hydration_note = table.Column<string>(type: "text", nullable: true),
                    treatment_provided = table.Column<string>(type: "text", nullable: true),
                    medication_given = table.Column<string>(type: "text", nullable: true),
                    veterinarian_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    veterinarian_note = table.Column<string>(type: "text", nullable: true),
                    location_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    general_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("HorseHealthLog_pkey", x => x.horse_health_log_id);
                    table.ForeignKey(
                        name: "HorseHealthLog_employee_id_fkey",
                        column: x => x.employee_id,
                        principalTable: "Employee",
                        principalColumn: "employee_id");
                    table.ForeignKey(
                        name: "HorseHealthLog_horse_id_fkey",
                        column: x => x.horse_id,
                        principalTable: "Horse",
                        principalColumn: "horse_id");
                    table.ForeignKey(
                        name: "HorseHealthLog_transport_id_fkey",
                        column: x => x.transport_id,
                        principalTable: "Transport",
                        principalColumn: "transport_id");
                });

            migrationBuilder.CreateTable(
                name: "Notification",
                columns: table => new
                {
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_type = table.Column<NotificationType>(type: "notification_type", nullable: false),
                    priority = table.Column<NotificationPriority>(type: "notification_priority", nullable: false, defaultValueSql: "'NORMAL'::notification_priority"),
                    transport_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    reference_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Notification_pkey", x => x.notification_id);
                    table.ForeignKey(
                        name: "Notification_transport_id_fkey",
                        column: x => x.transport_id,
                        principalTable: "Transport",
                        principalColumn: "transport_id");
                });

            migrationBuilder.CreateTable(
                name: "TransportPlan",
                columns: table => new
                {
                    transport_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<TransportPlanStatus>(type: "transport_plan_status", nullable: false, defaultValueSql: "'DRAFT'::transport_plan_status"),
                    transport_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    planned_start_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    planned_end_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    planning_note = table.Column<string>(type: "text", nullable: true),
                    estimated_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    estimated_distance_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    approved_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("TransportPlan_pkey", x => x.transport_plan_id);
                    table.ForeignKey(
                        name: "TransportPlan_approved_by_employee_id_fkey",
                        column: x => x.approved_by_employee_id,
                        principalTable: "Employee",
                        principalColumn: "employee_id");
                    table.ForeignKey(
                        name: "TransportPlan_transport_id_fkey",
                        column: x => x.transport_id,
                        principalTable: "Transport",
                        principalColumn: "transport_id");
                });

            migrationBuilder.CreateTable(
                name: "CostApprovalDecision",
                columns: table => new
                {
                    cost_approval_decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emergency_cost_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    decision_reason = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    decision = table.Column<ApprovalDecision>(type: "approval_decision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("CostApprovalDecision_pkey", x => x.cost_approval_decision_id);
                    table.ForeignKey(
                        name: "CostApprovalDecision_emergency_cost_request_id_fkey",
                        column: x => x.emergency_cost_request_id,
                        principalTable: "EmergencyCostRequest",
                        principalColumn: "emergency_cost_request_id");
                    table.ForeignKey(
                        name: "CostApprovalDecision_employee_id_fkey",
                        column: x => x.employee_id,
                        principalTable: "Employee",
                        principalColumn: "employee_id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceItem",
                columns: table => new
                {
                    invoice_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type = table.Column<InvoiceItemType>(type: "invoice_item_type", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    horse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    emergency_cost_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValueSql: "1"),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("InvoiceItem_pkey", x => x.invoice_item_id);
                    table.ForeignKey(
                        name: "InvoiceItem_emergency_cost_request_id_fkey",
                        column: x => x.emergency_cost_request_id,
                        principalTable: "EmergencyCostRequest",
                        principalColumn: "emergency_cost_request_id");
                    table.ForeignKey(
                        name: "InvoiceItem_horse_id_fkey",
                        column: x => x.horse_id,
                        principalTable: "Horse",
                        principalColumn: "horse_id");
                    table.ForeignKey(
                        name: "InvoiceItem_invoice_id_fkey",
                        column: x => x.invoice_id,
                        principalTable: "Invoice",
                        principalColumn: "invoice_id");
                });

            migrationBuilder.CreateTable(
                name: "AuthoritySubmission",
                columns: table => new
                {
                    authority_submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compliance_dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_no = table.Column<int>(type: "integer", nullable: false),
                    authority_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    submission_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    submission_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    response_received_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<AuthoritySubmissionStatus>(type: "authority_submission_status", nullable: false, defaultValueSql: "'DRAFT'::authority_submission_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("AuthoritySubmission_pkey", x => x.authority_submission_id);
                    table.ForeignKey(
                        name: "AuthoritySubmission_compliance_dossier_id_fkey",
                        column: x => x.compliance_dossier_id,
                        principalTable: "ComplianceDossier",
                        principalColumn: "compliance_dossier_id");
                },
                comment: "AuthoritySubmission: Một lần thực tế gửi ComplianceDossier tới cơ quan có thẩm quyền. Một dossier có thể được gửi nhiều lần nếu bị trả về hoặc từ chối.");

            migrationBuilder.CreateTable(
                name: "DossierRequirement",
                columns: table => new
                {
                    dossier_requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compliance_dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compliance_requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    instruction = table.Column<string>(type: "text", nullable: true),
                    satisfied_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    waived_reason = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<DossierRequirementStatus>(type: "dossier_requirement_status", nullable: false, defaultValueSql: "'PENDING'::dossier_requirement_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("DossierRequirement_pkey", x => x.dossier_requirement_id);
                    table.ForeignKey(
                        name: "DossierRequirement_compliance_dossier_id_fkey",
                        column: x => x.compliance_dossier_id,
                        principalTable: "ComplianceDossier",
                        principalColumn: "compliance_dossier_id");
                    table.ForeignKey(
                        name: "DossierRequirement_compliance_requirement_id_fkey",
                        column: x => x.compliance_requirement_id,
                        principalTable: "ComplianceRequirement",
                        principalColumn: "compliance_requirement_id");
                },
                comment: "DossierRequirement: Một yêu cầu pháp lý thực tế đã được sinh ra trong ComplianceDossier của chuyến đi cụ thể, dựa trên loại ComplianceRequirement.");

            migrationBuilder.CreateTable(
                name: "AccountNotification",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivered_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    read_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<RecipientStatus>(type: "recipient_status", nullable: false, defaultValueSql: "'PENDING'::recipient_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("AccountNotification_pkey", x => new { x.account_id, x.notification_id });
                    table.ForeignKey(
                        name: "AccountNotification_account_id_fkey",
                        column: x => x.account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                    table.ForeignKey(
                        name: "AccountNotification_notification_id_fkey",
                        column: x => x.notification_id,
                        principalTable: "Notification",
                        principalColumn: "notification_id");
                });

            migrationBuilder.CreateTable(
                name: "EmployeeTransportPlan",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transport_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsibility = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    assigned_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("EmployeeTransportPlan_pkey", x => new { x.employee_id, x.transport_plan_id });
                    table.ForeignKey(
                        name: "EmployeeTransportPlan_employee_id_fkey",
                        column: x => x.employee_id,
                        principalTable: "Employee",
                        principalColumn: "employee_id");
                    table.ForeignKey(
                        name: "EmployeeTransportPlan_transport_plan_id_fkey",
                        column: x => x.transport_plan_id,
                        principalTable: "TransportPlan",
                        principalColumn: "transport_plan_id");
                });

            migrationBuilder.CreateTable(
                name: "RoutePlan",
                columns: table => new
                {
                    route_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<RoutePlanStatus>(type: "route_plan_status", nullable: false, defaultValueSql: "'DRAFT'::route_plan_status"),
                    transport_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_plan_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    total_distance_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    estimated_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    route_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("RoutePlan_pkey", x => x.route_plan_id);
                    table.ForeignKey(
                        name: "RoutePlan_transport_plan_id_fkey",
                        column: x => x.transport_plan_id,
                        principalTable: "TransportPlan",
                        principalColumn: "transport_plan_id");
                },
                comment: "RoutePlan: Kế hoạch tuyến đường của một TransportPlan. Đây là tập hợp các Route được sắp xếp nối tiếp từ điểm đầu đến điểm cuối.");

            migrationBuilder.CreateTable(
                name: "TransportPlanProvider",
                columns: table => new
                {
                    transport_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transport_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    assigned_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("TransportPlanProvider_pkey", x => new { x.transport_plan_id, x.transport_provider_id });
                    table.ForeignKey(
                        name: "TransportPlanProvider_transport_plan_id_fkey",
                        column: x => x.transport_plan_id,
                        principalTable: "TransportPlan",
                        principalColumn: "transport_plan_id");
                    table.ForeignKey(
                        name: "TransportPlanProvider_transport_provider_id_fkey",
                        column: x => x.transport_provider_id,
                        principalTable: "TransportProvider",
                        principalColumn: "transport_provider_id");
                });

            migrationBuilder.CreateTable(
                name: "ComplianceDocument",
                columns: table => new
                {
                    compliance_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    document_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_number = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    issuing_authority = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    issued_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<ComplianceDocumentStatus>(type: "compliance_document_status", nullable: false, defaultValueSql: "'PROVIDED'::compliance_document_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("ComplianceDocument_pkey", x => x.compliance_document_id);
                    table.ForeignKey(
                        name: "ComplianceDocument_dossier_requirement_id_fkey",
                        column: x => x.dossier_requirement_id,
                        principalTable: "DossierRequirement",
                        principalColumn: "dossier_requirement_id");
                },
                comment: "ComplianceDocument: Giấy tờ thực tế được cung cấp để đáp ứng một DossierRequirement. Đây là document logic, không phải từng file upload.");

            migrationBuilder.CreateTable(
                name: "DocumentRequest",
                columns: table => new
                {
                    document_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    fulfilled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    request_type = table.Column<DocumentRequestType>(type: "document_request_type", nullable: false),
                    status = table.Column<DocumentRequestStatus>(type: "document_request_status", nullable: false, defaultValueSql: "'OPEN'::document_request_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("DocumentRequest_pkey", x => x.document_request_id);
                    table.ForeignKey(
                        name: "DocumentRequest_dossier_requirement_id_fkey",
                        column: x => x.dossier_requirement_id,
                        principalTable: "DossierRequirement",
                        principalColumn: "dossier_requirement_id");
                },
                comment: "DocumentRequest: Yêu cầu gửi tới Customer để cung cấp, sửa, upload lại hoặc bổ sung thông tin cho một DossierRequirement.");

            migrationBuilder.CreateTable(
                name: "RoutePlanRoute",
                columns: table => new
                {
                    route_plan_route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_type = table.Column<RouteType>(type: "route_type", nullable: false, defaultValueSql: "'PRIMARY'::route_type"),
                    route_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    driver_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    planned_departure_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    planned_arrival_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    actual_departure_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    actual_arrival_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("RoutePlanRoute_pkey", x => x.route_plan_route_id);
                    table.ForeignKey(
                        name: "RoutePlanRoute_driver_employee_id_fkey",
                        column: x => x.driver_employee_id,
                        principalTable: "Employee",
                        principalColumn: "employee_id");
                    table.ForeignKey(
                        name: "RoutePlanRoute_route_id_fkey",
                        column: x => x.route_id,
                        principalTable: "Route",
                        principalColumn: "route_id");
                    table.ForeignKey(
                        name: "RoutePlanRoute_route_plan_id_fkey",
                        column: x => x.route_plan_id,
                        principalTable: "RoutePlan",
                        principalColumn: "route_plan_id");
                },
                comment: "RoutePlanRoute: Bảng trung gian biểu diễn một Route được dùng trong một RoutePlan, gồm thứ tự đi, Driver phụ trách và thời gian dự kiến/thực tế của chính lần sử dụng Route đó.");

            migrationBuilder.CreateTable(
                name: "DocumentVersion",
                columns: table => new
                {
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compliance_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    upload_note = table.Column<string>(type: "text", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("DocumentVersion_pkey", x => x.document_version_id);
                    table.ForeignKey(
                        name: "DocumentVersion_compliance_document_id_fkey",
                        column: x => x.compliance_document_id,
                        principalTable: "ComplianceDocument",
                        principalColumn: "compliance_document_id");
                },
                comment: "DocumentVersion: Một phiên bản file cụ thể của ComplianceDocument. Khi giấy tờ bị sửa hoặc upload lại thì tạo version mới, không tạo ComplianceDocument mới.");

            migrationBuilder.CreateTable(
                name: "HandoverRecord",
                columns: table => new
                {
                    handover_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handover_type = table.Column<HandoverType>(type: "handover_type", nullable: false),
                    status = table.Column<HandoverStatus>(type: "handover_status", nullable: false, defaultValueSql: "'PLANNED'::handover_status"),
                    from_type = table.Column<HandoverPartyType>(type: "handover_party_type", nullable: false),
                    to_type = table.Column<HandoverPartyType>(type: "handover_party_type", nullable: false),
                    transport_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handover_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    planned_handover_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    actual_handover_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    from_customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_route_plan_route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_route_plan_route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_party_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    from_contact_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    from_contact_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_party_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    to_contact_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    to_contact_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    location_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    horse_condition_note = table.Column<string>(type: "text", nullable: true),
                    document_handover_note = table.Column<string>(type: "text", nullable: true),
                    equipment_handover_note = table.Column<string>(type: "text", nullable: true),
                    discrepancy_note = table.Column<string>(type: "text", nullable: true),
                    sender_signature_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    receiver_signature_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("HandoverRecord_pkey", x => x.handover_record_id);
                    table.ForeignKey(
                        name: "HandoverRecord_from_customer_id_fkey",
                        column: x => x.from_customer_id,
                        principalTable: "Customer",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "HandoverRecord_from_employee_id_fkey",
                        column: x => x.from_employee_id,
                        principalTable: "Employee",
                        principalColumn: "employee_id");
                    table.ForeignKey(
                        name: "HandoverRecord_from_route_plan_route_id_fkey",
                        column: x => x.from_route_plan_route_id,
                        principalTable: "RoutePlanRoute",
                        principalColumn: "route_plan_route_id");
                    table.ForeignKey(
                        name: "HandoverRecord_to_customer_id_fkey",
                        column: x => x.to_customer_id,
                        principalTable: "Customer",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "HandoverRecord_to_employee_id_fkey",
                        column: x => x.to_employee_id,
                        principalTable: "Employee",
                        principalColumn: "employee_id");
                    table.ForeignKey(
                        name: "HandoverRecord_to_route_plan_route_id_fkey",
                        column: x => x.to_route_plan_route_id,
                        principalTable: "RoutePlanRoute",
                        principalColumn: "route_plan_route_id");
                    table.ForeignKey(
                        name: "HandoverRecord_transport_id_fkey",
                        column: x => x.transport_id,
                        principalTable: "Transport",
                        principalColumn: "transport_id");
                },
                comment: "HandoverRecord: Một lần bàn giao trong Transport. from_type/to_type xác định bên giao/nhận là CUSTOMER hay EMPLOYEE; from/to_route_plan_route_id xác định chặng vừa kết thúc và chặng tiếp theo.");

            migrationBuilder.CreateTable(
                name: "RoutePlanRouteVehicle",
                columns: table => new
                {
                    route_plan_route_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("RoutePlanRouteVehicle_pkey", x => new { x.route_plan_route_id, x.vehicle_id });
                    table.ForeignKey(
                        name: "RoutePlanRouteVehicle_route_plan_route_id_fkey",
                        column: x => x.route_plan_route_id,
                        principalTable: "RoutePlanRoute",
                        principalColumn: "route_plan_route_id");
                    table.ForeignKey(
                        name: "RoutePlanRouteVehicle_vehicle_id_fkey",
                        column: x => x.vehicle_id,
                        principalTable: "Vehicle",
                        principalColumn: "vehicle_id");
                },
                comment: "RoutePlanRouteVehicle: Bảng trung gian N-N gán Vehicle vào một chặng RoutePlanRoute cụ thể. Nhờ vậy một Transport có thể đi xe -> máy bay -> xe, và cùng một Vehicle có thể được tái sử dụng ở nhiều chuyến/chặng khác nhau.");

            migrationBuilder.CreateTable(
                name: "DocumentReview",
                columns: table => new
                {
                    document_review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_comment = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    result = table.Column<DocumentReviewResult>(type: "document_review_result", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("DocumentReview_pkey", x => x.document_review_id);
                    table.ForeignKey(
                        name: "DocumentReview_document_version_id_fkey",
                        column: x => x.document_version_id,
                        principalTable: "DocumentVersion",
                        principalColumn: "document_version_id");
                },
                comment: "DocumentReview: Kết quả kiểm tra của một DocumentVersion cụ thể, ví dụ APPROVED, REJECTED hoặc REVISION_REQUIRED.");

            migrationBuilder.CreateTable(
                name: "Claim",
                columns: table => new
                {
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handover_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    requested_compensation_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    evidence_note = table.Column<string>(type: "text", nullable: true),
                    customer_note = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    claim_type = table.Column<ClaimType>(type: "claim_type", nullable: false),
                    status = table.Column<ClaimStatus>(type: "claim_status", nullable: false, defaultValueSql: "'PENDING'::claim_status")
                },
                constraints: table =>
                {
                    table.PrimaryKey("Claim_pkey", x => x.claim_id);
                    table.ForeignKey(
                        name: "Claim_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "Customer",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "Claim_handover_record_id_fkey",
                        column: x => x.handover_record_id,
                        principalTable: "HandoverRecord",
                        principalColumn: "handover_record_id");
                },
                comment: "Claim: Khiếu nại do Customer gửi liên quan đến một lần bàn giao. Hệ thống chỉ lưu khiếu nại và kết quả; việc trao đổi/thương lượng giữa Customer và Manager diễn ra ngoài hệ thống.");

            migrationBuilder.CreateTable(
                name: "ClaimResolution",
                columns: table => new
                {
                    claim_resolution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resolution_summary = table.Column<string>(type: "text", nullable: false),
                    approved_compensation_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    corrective_action = table.Column<string>(type: "text", nullable: true),
                    internal_note = table.Column<string>(type: "text", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    resolution_type = table.Column<ClaimResolutionType>(type: "claim_resolution_type", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("ClaimResolution_pkey", x => x.claim_resolution_id);
                    table.ForeignKey(
                        name: "ClaimResolution_claim_id_fkey",
                        column: x => x.claim_id,
                        principalTable: "Claim",
                        principalColumn: "claim_id");
                },
                comment: "ClaimResolution: Kết quả xử lý/thỏa thuận cuối cùng của một Claim, ví dụ mức bồi thường thực tế sau khi hai bên trao đổi trực tiếp.");

            migrationBuilder.CreateIndex(
                name: "Account_account_type_idx",
                table: "Account",
                column: "account_type");

            migrationBuilder.CreateIndex(
                name: "Account_email_key",
                table: "Account",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Account_is_deleted_idx",
                table: "Account",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "Account_status_idx",
                table: "Account",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_Account_deleted_by_account_id",
                table: "Account",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "AccountNotification_notification_id_idx",
                table: "AccountNotification",
                column: "notification_id");

            migrationBuilder.CreateIndex(
                name: "AccountNotification_read_at_idx",
                table: "AccountNotification",
                column: "read_at");

            migrationBuilder.CreateIndex(
                name: "AccountNotification_status_idx",
                table: "AccountNotification",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "AuthoritySubmission_compliance_dossier_id_idx",
                table: "AuthoritySubmission",
                column: "compliance_dossier_id");

            migrationBuilder.CreateIndex(
                name: "AuthoritySubmission_compliance_dossier_id_submission_no_idx",
                table: "AuthoritySubmission",
                columns: new[] { "compliance_dossier_id", "submission_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AuthoritySubmission_status_idx",
                table: "AuthoritySubmission",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "AuthoritySubmission_submission_reference_idx",
                table: "AuthoritySubmission",
                column: "submission_reference");

            migrationBuilder.CreateIndex(
                name: "Booking_assigned_employee_id_idx",
                table: "Booking",
                column: "assigned_employee_id");

            migrationBuilder.CreateIndex(
                name: "Booking_booking_code_key",
                table: "Booking",
                column: "booking_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Booking_customer_id_idx",
                table: "Booking",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "Booking_requested_departure_date_idx",
                table: "Booking",
                column: "requested_departure_date");

            migrationBuilder.CreateIndex(
                name: "Booking_status_idx",
                table: "Booking",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "BookingHorse_horse_id_idx",
                table: "BookingHorse",
                column: "horse_id");

            migrationBuilder.CreateIndex(
                name: "BookingStatusHistory_booking_id_idx",
                table: "BookingStatusHistory",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "BookingStatusHistory_changed_at_idx",
                table: "BookingStatusHistory",
                column: "changed_at");

            migrationBuilder.CreateIndex(
                name: "BookingStatusHistory_changed_by_account_id_idx",
                table: "BookingStatusHistory",
                column: "changed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "BookingStatusHistory_type_idx",
                table: "BookingStatusHistory",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "Claim_claim_code_key",
                table: "Claim",
                column: "claim_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Claim_claim_type_idx",
                table: "Claim",
                column: "claim_type");

            migrationBuilder.CreateIndex(
                name: "Claim_customer_id_idx",
                table: "Claim",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "Claim_handover_record_id_idx",
                table: "Claim",
                column: "handover_record_id");

            migrationBuilder.CreateIndex(
                name: "Claim_status_idx",
                table: "Claim",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "Claim_submitted_at_idx",
                table: "Claim",
                column: "submitted_at");

            migrationBuilder.CreateIndex(
                name: "ClaimResolution_claim_id_key",
                table: "ClaimResolution",
                column: "claim_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ClaimResolution_resolution_type_idx",
                table: "ClaimResolution",
                column: "resolution_type");

            migrationBuilder.CreateIndex(
                name: "ClaimResolution_resolved_at_idx",
                table: "ClaimResolution",
                column: "resolved_at");

            migrationBuilder.CreateIndex(
                name: "ComplianceDocument_document_code_key",
                table: "ComplianceDocument",
                column: "document_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ComplianceDocument_document_number_idx",
                table: "ComplianceDocument",
                column: "document_number");

            migrationBuilder.CreateIndex(
                name: "ComplianceDocument_dossier_requirement_id_key",
                table: "ComplianceDocument",
                column: "dossier_requirement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ComplianceDocument_expiry_date_idx",
                table: "ComplianceDocument",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "ComplianceDocument_status_idx",
                table: "ComplianceDocument",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ComplianceDossier_dossier_code_key",
                table: "ComplianceDossier",
                column: "dossier_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ComplianceDossier_status_idx",
                table: "ComplianceDossier",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ComplianceDossier_transport_id_key",
                table: "ComplianceDossier",
                column: "transport_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ComplianceRequirement_is_deleted_idx",
                table: "ComplianceRequirement",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "ComplianceRequirement_requirement_code_key",
                table: "ComplianceRequirement",
                column: "requirement_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ComplianceRequirement_requirement_name_idx",
                table: "ComplianceRequirement",
                column: "requirement_name");

            migrationBuilder.CreateIndex(
                name: "ComplianceRequirement_status_idx",
                table: "ComplianceRequirement",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceRequirement_deleted_by_account_id",
                table: "ComplianceRequirement",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "CostApprovalDecision_decided_at_idx",
                table: "CostApprovalDecision",
                column: "decided_at");

            migrationBuilder.CreateIndex(
                name: "CostApprovalDecision_decision_idx",
                table: "CostApprovalDecision",
                column: "decision");

            migrationBuilder.CreateIndex(
                name: "CostApprovalDecision_emergency_cost_request_id_key",
                table: "CostApprovalDecision",
                column: "emergency_cost_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "CostApprovalDecision_employee_id_idx",
                table: "CostApprovalDecision",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "Customer_account_id_key",
                table: "Customer",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Customer_is_deleted_idx",
                table: "Customer",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "Customer_phone_idx",
                table: "Customer",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_deleted_by_account_id",
                table: "Customer",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "DocumentRequest_dossier_requirement_id_idx",
                table: "DocumentRequest",
                column: "dossier_requirement_id");

            migrationBuilder.CreateIndex(
                name: "DocumentRequest_request_code_key",
                table: "DocumentRequest",
                column: "request_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "DocumentRequest_request_type_idx",
                table: "DocumentRequest",
                column: "request_type");

            migrationBuilder.CreateIndex(
                name: "DocumentRequest_requested_at_idx",
                table: "DocumentRequest",
                column: "requested_at");

            migrationBuilder.CreateIndex(
                name: "DocumentRequest_status_idx",
                table: "DocumentRequest",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "DocumentReview_document_version_id_idx",
                table: "DocumentReview",
                column: "document_version_id");

            migrationBuilder.CreateIndex(
                name: "DocumentReview_result_idx",
                table: "DocumentReview",
                column: "result");

            migrationBuilder.CreateIndex(
                name: "DocumentReview_reviewed_at_idx",
                table: "DocumentReview",
                column: "reviewed_at");

            migrationBuilder.CreateIndex(
                name: "DocumentVersion_compliance_document_id_idx",
                table: "DocumentVersion",
                column: "compliance_document_id");

            migrationBuilder.CreateIndex(
                name: "DocumentVersion_compliance_document_id_version_no_idx",
                table: "DocumentVersion",
                columns: new[] { "compliance_document_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "DossierRequirement_compliance_dossier_id_compliance_require_idx",
                table: "DossierRequirement",
                columns: new[] { "compliance_dossier_id", "compliance_requirement_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "DossierRequirement_compliance_dossier_id_idx",
                table: "DossierRequirement",
                column: "compliance_dossier_id");

            migrationBuilder.CreateIndex(
                name: "DossierRequirement_compliance_requirement_id_idx",
                table: "DossierRequirement",
                column: "compliance_requirement_id");

            migrationBuilder.CreateIndex(
                name: "DossierRequirement_status_idx",
                table: "DossierRequirement",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "DossierTemplate_is_deleted_idx",
                table: "DossierTemplate",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "DossierTemplate_status_idx",
                table: "DossierTemplate",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "DossierTemplate_template_code_key",
                table: "DossierTemplate",
                column: "template_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "DossierTemplate_template_name_idx",
                table: "DossierTemplate",
                column: "template_name");

            migrationBuilder.CreateIndex(
                name: "IX_DossierTemplate_deleted_by_account_id",
                table: "DossierTemplate",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "DossierTemplateItem_compliance_requirement_id_idx",
                table: "DossierTemplateItem",
                column: "compliance_requirement_id");

            migrationBuilder.CreateIndex(
                name: "DossierTemplateItem_dossier_template_id_compliance_requirem_idx",
                table: "DossierTemplateItem",
                columns: new[] { "dossier_template_id", "compliance_requirement_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "DossierTemplateItem_dossier_template_id_idx",
                table: "DossierTemplateItem",
                column: "dossier_template_id");

            migrationBuilder.CreateIndex(
                name: "DossierTemplateItem_dossier_template_id_sequence_no_idx",
                table: "DossierTemplateItem",
                columns: new[] { "dossier_template_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmergencyCostRequest_category_idx",
                table: "EmergencyCostRequest",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "EmergencyCostRequest_incident_report_id_idx",
                table: "EmergencyCostRequest",
                column: "incident_report_id");

            migrationBuilder.CreateIndex(
                name: "EmergencyCostRequest_request_code_key",
                table: "EmergencyCostRequest",
                column: "request_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmergencyCostRequest_status_idx",
                table: "EmergencyCostRequest",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "Employee_account_id_key",
                table: "Employee",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Employee_employee_code_key",
                table: "Employee",
                column: "employee_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Employee_is_active_idx",
                table: "Employee",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "Employee_is_deleted_idx",
                table: "Employee",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "Employee_role_idx",
                table: "Employee",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "IX_Employee_deleted_by_account_id",
                table: "Employee",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "EmployeeTransportPlan_transport_plan_id_idx",
                table: "EmployeeTransportPlan",
                column: "transport_plan_id");

            migrationBuilder.CreateIndex(
                name: "FinancialReport_report_code_key",
                table: "FinancialReport",
                column: "report_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "FinancialReport_transport_id_key",
                table: "FinancialReport",
                column: "transport_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_actual_handover_at_idx",
                table: "HandoverRecord",
                column: "actual_handover_at");

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_from_customer_id_idx",
                table: "HandoverRecord",
                column: "from_customer_id");

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_from_employee_id_idx",
                table: "HandoverRecord",
                column: "from_employee_id");

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_from_route_plan_route_id_idx",
                table: "HandoverRecord",
                column: "from_route_plan_route_id");

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_from_type_idx",
                table: "HandoverRecord",
                column: "from_type");

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_handover_code_key",
                table: "HandoverRecord",
                column: "handover_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_status_idx",
                table: "HandoverRecord",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_to_customer_id_idx",
                table: "HandoverRecord",
                column: "to_customer_id");

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_to_employee_id_idx",
                table: "HandoverRecord",
                column: "to_employee_id");

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_to_route_plan_route_id_idx",
                table: "HandoverRecord",
                column: "to_route_plan_route_id");

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_to_type_idx",
                table: "HandoverRecord",
                column: "to_type");

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_transport_id_idx",
                table: "HandoverRecord",
                column: "transport_id");

            migrationBuilder.CreateIndex(
                name: "HandoverRecord_transport_id_sequence_no_idx",
                table: "HandoverRecord",
                columns: new[] { "transport_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Horse_customer_id_idx",
                table: "Horse",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "Horse_horse_name_idx",
                table: "Horse",
                column: "horse_name");

            migrationBuilder.CreateIndex(
                name: "Horse_is_deleted_idx",
                table: "Horse",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "Horse_microchip_number_key",
                table: "Horse",
                column: "microchip_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Horse_passport_number_key",
                table: "Horse",
                column: "passport_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Horse_deleted_by_account_id",
                table: "Horse",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "HorseHealthIncidentDetail_horse_id_idx",
                table: "HorseHealthIncidentDetail",
                column: "horse_id");

            migrationBuilder.CreateIndex(
                name: "HorseHealthIncidentDetail_incident_report_id_horse_id_idx",
                table: "HorseHealthIncidentDetail",
                columns: new[] { "incident_report_id", "horse_id" });

            migrationBuilder.CreateIndex(
                name: "HorseHealthIncidentDetail_incident_report_id_idx",
                table: "HorseHealthIncidentDetail",
                column: "incident_report_id");

            migrationBuilder.CreateIndex(
                name: "HorseHealthIncidentDetail_incident_type_idx",
                table: "HorseHealthIncidentDetail",
                column: "incident_type");

            migrationBuilder.CreateIndex(
                name: "HorseHealthLog_checked_at_idx",
                table: "HorseHealthLog",
                column: "checked_at");

            migrationBuilder.CreateIndex(
                name: "HorseHealthLog_employee_id_idx",
                table: "HorseHealthLog",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "HorseHealthLog_health_status_idx",
                table: "HorseHealthLog",
                column: "health_status");

            migrationBuilder.CreateIndex(
                name: "HorseHealthLog_horse_id_idx",
                table: "HorseHealthLog",
                column: "horse_id");

            migrationBuilder.CreateIndex(
                name: "HorseHealthLog_transport_id_horse_id_idx",
                table: "HorseHealthLog",
                columns: new[] { "transport_id", "horse_id" });

            migrationBuilder.CreateIndex(
                name: "HorseHealthLog_transport_id_idx",
                table: "HorseHealthLog",
                column: "transport_id");

            migrationBuilder.CreateIndex(
                name: "IncidentReport_category_idx",
                table: "IncidentReport",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IncidentReport_employee_id_idx",
                table: "IncidentReport",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "IncidentReport_incident_code_key",
                table: "IncidentReport",
                column: "incident_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IncidentReport_location_id_idx",
                table: "IncidentReport",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IncidentReport_occurred_at_idx",
                table: "IncidentReport",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IncidentReport_severity_idx",
                table: "IncidentReport",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "IncidentReport_status_idx",
                table: "IncidentReport",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "Invoice_booking_id_idx",
                table: "Invoice",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "Invoice_due_date_idx",
                table: "Invoice",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "Invoice_employee_id_idx",
                table: "Invoice",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "Invoice_invoice_code_key",
                table: "Invoice",
                column: "invoice_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Invoice_status_idx",
                table: "Invoice",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "InvoiceItem_emergency_cost_request_id_key",
                table: "InvoiceItem",
                column: "emergency_cost_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "InvoiceItem_horse_id_idx",
                table: "InvoiceItem",
                column: "horse_id");

            migrationBuilder.CreateIndex(
                name: "InvoiceItem_invoice_id_idx",
                table: "InvoiceItem",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "InvoiceItem_item_type_idx",
                table: "InvoiceItem",
                column: "item_type");

            migrationBuilder.CreateIndex(
                name: "InvoiceStatusHistory_changed_at_idx",
                table: "InvoiceStatusHistory",
                column: "changed_at");

            migrationBuilder.CreateIndex(
                name: "InvoiceStatusHistory_changed_by_account_id_idx",
                table: "InvoiceStatusHistory",
                column: "changed_by_account_id");

            migrationBuilder.CreateIndex(
                name: "InvoiceStatusHistory_invoice_id_idx",
                table: "InvoiceStatusHistory",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "InvoiceStatusHistory_new_status_idx",
                table: "InvoiceStatusHistory",
                column: "new_status");

            migrationBuilder.CreateIndex(
                name: "InvoiceStatusHistory_type_idx",
                table: "InvoiceStatusHistory",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "IX_Location_deleted_by_account_id",
                table: "Location",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "Location_country_idx",
                table: "Location",
                column: "country");

            migrationBuilder.CreateIndex(
                name: "Location_is_deleted_idx",
                table: "Location",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "Location_location_name_idx",
                table: "Location",
                column: "location_name");

            migrationBuilder.CreateIndex(
                name: "Location_location_type_idx",
                table: "Location",
                column: "location_type");

            migrationBuilder.CreateIndex(
                name: "Notification_created_at_idx",
                table: "Notification",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "Notification_notification_type_idx",
                table: "Notification",
                column: "notification_type");

            migrationBuilder.CreateIndex(
                name: "Notification_priority_idx",
                table: "Notification",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "Notification_reference_type_reference_id_idx",
                table: "Notification",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "Notification_transport_id_idx",
                table: "Notification",
                column: "transport_id");

            migrationBuilder.CreateIndex(
                name: "Receipt_invoice_id_idx",
                table: "Receipt",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "Receipt_paid_at_idx",
                table: "Receipt",
                column: "paid_at");

            migrationBuilder.CreateIndex(
                name: "Receipt_receipt_code_key",
                table: "Receipt",
                column: "receipt_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Receipt_status_idx",
                table: "Receipt",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "Receipt_transaction_reference_idx",
                table: "Receipt",
                column: "transaction_reference");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_replaced_by_token_id",
                table: "RefreshToken",
                column: "replaced_by_token_id");

            migrationBuilder.CreateIndex(
                name: "RefreshToken_account_id_idx",
                table: "RefreshToken",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "RefreshToken_expires_at_idx",
                table: "RefreshToken",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "RefreshToken_revoked_at_idx",
                table: "RefreshToken",
                column: "revoked_at");

            migrationBuilder.CreateIndex(
                name: "RefreshToken_token_hash_key",
                table: "RefreshToken",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Route_deleted_by_account_id",
                table: "Route",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "Route_from_location_id_idx",
                table: "Route",
                column: "from_location_id");

            migrationBuilder.CreateIndex(
                name: "Route_is_deleted_idx",
                table: "Route",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "Route_route_code_key",
                table: "Route",
                column: "route_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Route_to_location_id_idx",
                table: "Route",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "Route_transport_mode_idx",
                table: "Route",
                column: "transport_mode");

            migrationBuilder.CreateIndex(
                name: "RoutePlan_route_plan_code_key",
                table: "RoutePlan",
                column: "route_plan_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "RoutePlan_status_idx",
                table: "RoutePlan",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "RoutePlan_transport_plan_id_key",
                table: "RoutePlan",
                column: "transport_plan_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "RoutePlanRoute_driver_employee_id_idx",
                table: "RoutePlanRoute",
                column: "driver_employee_id");

            migrationBuilder.CreateIndex(
                name: "RoutePlanRoute_route_id_idx",
                table: "RoutePlanRoute",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "RoutePlanRoute_route_plan_id_idx",
                table: "RoutePlanRoute",
                column: "route_plan_id");

            migrationBuilder.CreateIndex(
                name: "RoutePlanRoute_route_plan_id_sequence_no_idx",
                table: "RoutePlanRoute",
                columns: new[] { "route_plan_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "RoutePlanRoute_route_type_idx",
                table: "RoutePlanRoute",
                column: "route_type");

            migrationBuilder.CreateIndex(
                name: "RoutePlanRouteVehicle_vehicle_id_idx",
                table: "RoutePlanRouteVehicle",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "Transport_booking_id_key",
                table: "Transport",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Transport_status_idx",
                table: "Transport",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "Transport_transport_code_key",
                table: "Transport",
                column: "transport_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "TransportPlan_approved_by_employee_id_idx",
                table: "TransportPlan",
                column: "approved_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "TransportPlan_plan_code_key",
                table: "TransportPlan",
                column: "plan_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "TransportPlan_status_idx",
                table: "TransportPlan",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "TransportPlan_transport_id_idx",
                table: "TransportPlan",
                column: "transport_id");

            migrationBuilder.CreateIndex(
                name: "TransportPlanProvider_transport_provider_id_idx",
                table: "TransportPlanProvider",
                column: "transport_provider_id");

            migrationBuilder.CreateIndex(
                name: "IX_TransportProvider_deleted_by_account_id",
                table: "TransportProvider",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "TransportProvider_is_deleted_idx",
                table: "TransportProvider",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "TransportProvider_provider_code_key",
                table: "TransportProvider",
                column: "provider_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "TransportProvider_provider_name_idx",
                table: "TransportProvider",
                column: "provider_name");

            migrationBuilder.CreateIndex(
                name: "TransportProvider_status_idx",
                table: "TransportProvider",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicle_deleted_by_account_id",
                table: "Vehicle",
                column: "deleted_by_account_id");

            migrationBuilder.CreateIndex(
                name: "Vehicle_is_deleted_idx",
                table: "Vehicle",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "Vehicle_license_plate_key",
                table: "Vehicle",
                column: "license_plate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Vehicle_status_idx",
                table: "Vehicle",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "Vehicle_transport_mode_idx",
                table: "Vehicle",
                column: "transport_mode");

            migrationBuilder.CreateIndex(
                name: "Vehicle_vehicle_code_key",
                table: "Vehicle",
                column: "vehicle_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "VehicleIncidentDetail_incident_report_id_key",
                table: "VehicleIncidentDetail",
                column: "incident_report_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "VehicleIncidentDetail_incident_type_idx",
                table: "VehicleIncidentDetail",
                column: "incident_type");

            migrationBuilder.CreateIndex(
                name: "VehicleIncidentDetail_vehicle_id_idx",
                table: "VehicleIncidentDetail",
                column: "vehicle_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountNotification");

            migrationBuilder.DropTable(
                name: "AuthoritySubmission");

            migrationBuilder.DropTable(
                name: "BookingHorse");

            migrationBuilder.DropTable(
                name: "BookingStatusHistory");

            migrationBuilder.DropTable(
                name: "ClaimResolution");

            migrationBuilder.DropTable(
                name: "CostApprovalDecision");

            migrationBuilder.DropTable(
                name: "DocumentRequest");

            migrationBuilder.DropTable(
                name: "DocumentReview");

            migrationBuilder.DropTable(
                name: "DossierTemplateItem");

            migrationBuilder.DropTable(
                name: "EmployeeTransportPlan");

            migrationBuilder.DropTable(
                name: "FinancialReport");

            migrationBuilder.DropTable(
                name: "HorseHealthIncidentDetail");

            migrationBuilder.DropTable(
                name: "HorseHealthLog");

            migrationBuilder.DropTable(
                name: "InvoiceItem");

            migrationBuilder.DropTable(
                name: "InvoiceStatusHistory");

            migrationBuilder.DropTable(
                name: "Receipt");

            migrationBuilder.DropTable(
                name: "RefreshToken");

            migrationBuilder.DropTable(
                name: "RoutePlanRouteVehicle");

            migrationBuilder.DropTable(
                name: "TransportPlanProvider");

            migrationBuilder.DropTable(
                name: "VehicleIncidentDetail");

            migrationBuilder.DropTable(
                name: "Notification");

            migrationBuilder.DropTable(
                name: "Claim");

            migrationBuilder.DropTable(
                name: "DocumentVersion");

            migrationBuilder.DropTable(
                name: "DossierTemplate");

            migrationBuilder.DropTable(
                name: "EmergencyCostRequest");

            migrationBuilder.DropTable(
                name: "Horse");

            migrationBuilder.DropTable(
                name: "Invoice");

            migrationBuilder.DropTable(
                name: "TransportProvider");

            migrationBuilder.DropTable(
                name: "Vehicle");

            migrationBuilder.DropTable(
                name: "HandoverRecord");

            migrationBuilder.DropTable(
                name: "ComplianceDocument");

            migrationBuilder.DropTable(
                name: "IncidentReport");

            migrationBuilder.DropTable(
                name: "RoutePlanRoute");

            migrationBuilder.DropTable(
                name: "DossierRequirement");

            migrationBuilder.DropTable(
                name: "Route");

            migrationBuilder.DropTable(
                name: "RoutePlan");

            migrationBuilder.DropTable(
                name: "ComplianceDossier");

            migrationBuilder.DropTable(
                name: "ComplianceRequirement");

            migrationBuilder.DropTable(
                name: "Location");

            migrationBuilder.DropTable(
                name: "TransportPlan");

            migrationBuilder.DropTable(
                name: "Transport");

            migrationBuilder.DropTable(
                name: "Booking");

            migrationBuilder.DropTable(
                name: "Employee");

            migrationBuilder.DropTable(
                name: "Customer");

            migrationBuilder.DropTable(
                name: "Account");
        }
    }
}
