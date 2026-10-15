using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;

#nullable disable

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260928090000_AddComplianceV6Proofs")]
public partial class AddComplianceV6Proofs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE TYPE authority_evidence_type AS ENUM ('Acceptance', 'Rejection', 'AdditionalInformation', 'Other');");
        migrationBuilder.Sql("CREATE TYPE audit_visibility AS ENUM ('Public', 'Internal');");
        migrationBuilder.Sql("CREATE TYPE compliance_requirement_category AS ENUM ('Quarantine', 'Customs', 'Transport', 'HorseHealth', 'HorseIdentity', 'Other');");
        migrationBuilder.Sql("CREATE TYPE customs_clearance_proof_status AS ENUM ('PendingVerification', 'Cleared', 'Held', 'Rejected');");
        migrationBuilder.Sql("CREATE TYPE customs_lane AS ENUM ('GreenLane', 'OrangeLane', 'RedLane');");
        migrationBuilder.Sql("CREATE TYPE quarantine_proof_status AS ENUM ('PendingVerification', 'Passed', 'PassedWithConditions', 'Holdback', 'Failed', 'Rejected');");

        migrationBuilder.AddColumn<string>(
            name: "country",
            table: "ComplianceRequirement",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<ComplianceRequirementCategory>(
            name: "category",
            table: "ComplianceRequirement",
            type: "compliance_requirement_category",
            nullable: false,
            defaultValue: ComplianceRequirementCategory.Other);

        migrationBuilder.AddColumn<int>(
            name: "validity_period_days",
            table: "ComplianceRequirement",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "horse_id",
            table: "DossierRequirement",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "valid_until",
            table: "DossierRequirement",
            type: "date",
            nullable: true);

        migrationBuilder.DropIndex(
            name: "DossierRequirement_compliance_dossier_id_compliance_require_idx",
            table: "DossierRequirement");

        migrationBuilder.CreateIndex(
            name: "DossierRequirement_dossier_requirement_global_key",
            table: "DossierRequirement",
            columns: new[] { "compliance_dossier_id", "compliance_requirement_id" },
            unique: true,
            filter: "horse_id IS NULL");

        migrationBuilder.CreateIndex(
            name: "DossierRequirement_dossier_requirement_horse_key",
            table: "DossierRequirement",
            columns: new[] { "compliance_dossier_id", "compliance_requirement_id", "horse_id" },
            unique: true,
            filter: "horse_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "DossierRequirement_horse_id_idx",
            table: "DossierRequirement",
            column: "horse_id");

        migrationBuilder.CreateTable(
            name: "AuthoritySubmissionEvidenceFile",
            columns: table => new
            {
                authority_submission_evidence_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                authority_submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                evidence_type = table.Column<AuthorityEvidenceType>(type: "authority_evidence_type", nullable: false),
                uploaded_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                file_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                note = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("AuthoritySubmissionEvidenceFile_pkey", x => x.authority_submission_evidence_file_id);
                table.ForeignKey("AuthoritySubmissionEvidenceFile_authority_submission_id_fkey", x => x.authority_submission_id, "AuthoritySubmission", "authority_submission_id");
                table.ForeignKey("AuthoritySubmissionEvidenceFile_uploaded_by_employee_id_fkey", x => x.uploaded_by_employee_id, "Employee", "employee_id");
            });

        migrationBuilder.CreateIndex("AuthoritySubmissionEvidenceFile_authority_submission_id_idx", "AuthoritySubmissionEvidenceFile", "authority_submission_id");
        migrationBuilder.CreateIndex("AuthoritySubmissionEvidenceFile_uploaded_by_employee_id_idx", "AuthoritySubmissionEvidenceFile", "uploaded_by_employee_id");

        migrationBuilder.CreateTable(
            name: "QuarantineProof",
            columns: table => new
            {
                quarantine_proof_id = table.Column<Guid>(type: "uuid", nullable: false),
                compliance_dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<QuarantineProofStatus>(type: "quarantine_proof_status", nullable: false, defaultValue: QuarantineProofStatus.PendingVerification),
                submitted_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                verified_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                location_id = table.Column<Guid>(type: "uuid", nullable: true),
                bcp_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                official_veterinarian_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                bcp_receipt_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                microchip_checklist = table.Column<string>(type: "jsonb", nullable: true),
                inspection_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                submitted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                verified_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                conditions = table.Column<string>(type: "text", nullable: true),
                verification_note = table.Column<string>(type: "text", nullable: true),
                rejection_reason = table.Column<string>(type: "text", nullable: true),
                is_finalized = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                finalized_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("QuarantineProof_pkey", x => x.quarantine_proof_id);
                table.ForeignKey("QuarantineProof_compliance_dossier_id_fkey", x => x.compliance_dossier_id, "ComplianceDossier", "compliance_dossier_id");
                table.ForeignKey("QuarantineProof_submitted_by_employee_id_fkey", x => x.submitted_by_employee_id, "Employee", "employee_id");
                table.ForeignKey("QuarantineProof_verified_by_employee_id_fkey", x => x.verified_by_employee_id, "Employee", "employee_id");
                table.ForeignKey("QuarantineProof_location_id_fkey", x => x.location_id, "Location", "location_id");
            });

        migrationBuilder.CreateIndex("QuarantineProof_compliance_dossier_id_idx", "QuarantineProof", "compliance_dossier_id");
        migrationBuilder.CreateIndex("QuarantineProof_submitted_by_employee_id_idx", "QuarantineProof", "submitted_by_employee_id");
        migrationBuilder.CreateIndex("QuarantineProof_verified_by_employee_id_idx", "QuarantineProof", "verified_by_employee_id");
        migrationBuilder.CreateIndex("QuarantineProof_location_id_idx", "QuarantineProof", "location_id");

        migrationBuilder.CreateTable(
            name: "CustomsClearanceProof",
            columns: table => new
            {
                customs_clearance_proof_id = table.Column<Guid>(type: "uuid", nullable: false),
                compliance_dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<CustomsClearanceProofStatus>(type: "customs_clearance_proof_status", nullable: false, defaultValue: CustomsClearanceProofStatus.PendingVerification),
                customs_lane = table.Column<CustomsLane>(type: "customs_lane", nullable: true),
                submitted_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                verified_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                location_id = table.Column<Guid>(type: "uuid", nullable: true),
                customs_port_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                mrn_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                clearance_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                submitted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                verified_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                verification_note = table.Column<string>(type: "text", nullable: true),
                rejection_reason = table.Column<string>(type: "text", nullable: true),
                is_finalized = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                finalized_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("CustomsClearanceProof_pkey", x => x.customs_clearance_proof_id);
                table.ForeignKey("CustomsClearanceProof_compliance_dossier_id_fkey", x => x.compliance_dossier_id, "ComplianceDossier", "compliance_dossier_id");
                table.ForeignKey("CustomsClearanceProof_submitted_by_employee_id_fkey", x => x.submitted_by_employee_id, "Employee", "employee_id");
                table.ForeignKey("CustomsClearanceProof_verified_by_employee_id_fkey", x => x.verified_by_employee_id, "Employee", "employee_id");
                table.ForeignKey("CustomsClearanceProof_location_id_fkey", x => x.location_id, "Location", "location_id");
            });

        migrationBuilder.CreateIndex("CustomsClearanceProof_compliance_dossier_id_idx", "CustomsClearanceProof", "compliance_dossier_id");
        migrationBuilder.CreateIndex("CustomsClearanceProof_submitted_by_employee_id_idx", "CustomsClearanceProof", "submitted_by_employee_id");
        migrationBuilder.CreateIndex("CustomsClearanceProof_verified_by_employee_id_idx", "CustomsClearanceProof", "verified_by_employee_id");
        migrationBuilder.CreateIndex("CustomsClearanceProof_location_id_idx", "CustomsClearanceProof", "location_id");

        migrationBuilder.CreateTable(
            name: "ProofAttachment",
            columns: table => new
            {
                proof_attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                quarantine_proof_id = table.Column<Guid>(type: "uuid", nullable: true),
                customs_clearance_proof_id = table.Column<Guid>(type: "uuid", nullable: true),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                file_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                sort_order = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("ProofAttachment_pkey", x => x.proof_attachment_id);
                table.ForeignKey("ProofAttachment_quarantine_proof_id_fkey", x => x.quarantine_proof_id, "QuarantineProof", "quarantine_proof_id");
                table.ForeignKey("ProofAttachment_customs_clearance_proof_id_fkey", x => x.customs_clearance_proof_id, "CustomsClearanceProof", "customs_clearance_proof_id");
                table.CheckConstraint("ProofAttachment_exactly_one_proof_check", "(quarantine_proof_id IS NOT NULL) <> (customs_clearance_proof_id IS NOT NULL)");
            });

        migrationBuilder.CreateIndex("ProofAttachment_quarantine_proof_id_sort_order_key", "ProofAttachment", new[] { "quarantine_proof_id", "sort_order" }, unique: true, filter: "quarantine_proof_id IS NOT NULL");
        migrationBuilder.CreateIndex("ProofAttachment_customs_clearance_proof_id_sort_order_key", "ProofAttachment", new[] { "customs_clearance_proof_id", "sort_order" }, unique: true, filter: "customs_clearance_proof_id IS NOT NULL");

        migrationBuilder.CreateTable(
            name: "AuditDossier",
            columns: table => new
            {
                audit_dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                compliance_dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                action_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                target_entity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                target_id = table.Column<Guid>(type: "uuid", nullable: true),
                actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                summary = table.Column<string>(type: "text", nullable: true),
                details = table.Column<string>(type: "jsonb", nullable: true),
                visibility = table.Column<AuditVisibility>(type: "audit_visibility", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("AuditDossier_pkey", x => x.audit_dossier_id);
                table.ForeignKey("AuditDossier_compliance_dossier_id_fkey", x => x.compliance_dossier_id, "ComplianceDossier", "compliance_dossier_id");
                table.ForeignKey("AuditDossier_actor_account_id_fkey", x => x.actor_account_id, "Account", "account_id");
            });

        migrationBuilder.CreateIndex("AuditDossier_compliance_dossier_id_idx", "AuditDossier", "compliance_dossier_id");
        migrationBuilder.CreateIndex("AuditDossier_actor_account_id_idx", "AuditDossier", "actor_account_id");
        migrationBuilder.CreateIndex("AuditDossier_target_entity_target_id_idx", "AuditDossier", new[] { "target_entity", "target_id" });

        migrationBuilder.AddForeignKey(
            name: "DossierRequirement_horse_id_fkey",
            table: "DossierRequirement",
            column: "horse_id",
            principalTable: "Horse",
            principalColumn: "horse_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("DossierRequirement_horse_id_fkey", "DossierRequirement");
        migrationBuilder.DropTable("AuditDossier");
        migrationBuilder.DropTable("ProofAttachment");
        migrationBuilder.DropTable("CustomsClearanceProof");
        migrationBuilder.DropTable("QuarantineProof");
        migrationBuilder.DropTable("AuthoritySubmissionEvidenceFile");

        migrationBuilder.DropIndex("DossierRequirement_dossier_requirement_global_key", "DossierRequirement");
        migrationBuilder.DropIndex("DossierRequirement_dossier_requirement_horse_key", "DossierRequirement");
        migrationBuilder.DropIndex("DossierRequirement_horse_id_idx", "DossierRequirement");

        migrationBuilder.CreateIndex(
            name: "DossierRequirement_compliance_dossier_id_compliance_require_idx",
            table: "DossierRequirement",
            columns: new[] { "compliance_dossier_id", "compliance_requirement_id" },
            unique: true);

        migrationBuilder.DropColumn("country", "ComplianceRequirement");
        migrationBuilder.DropColumn("category", "ComplianceRequirement");
        migrationBuilder.DropColumn("validity_period_days", "ComplianceRequirement");
        migrationBuilder.DropColumn("horse_id", "DossierRequirement");
        migrationBuilder.DropColumn("valid_until", "DossierRequirement");

        migrationBuilder.Sql("DROP TYPE quarantine_proof_status;");
        migrationBuilder.Sql("DROP TYPE customs_lane;");
        migrationBuilder.Sql("DROP TYPE customs_clearance_proof_status;");
        migrationBuilder.Sql("DROP TYPE compliance_requirement_category;");
        migrationBuilder.Sql("DROP TYPE audit_visibility;");
        migrationBuilder.Sql("DROP TYPE authority_evidence_type;");
    }
}
