using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pegaxus.Repository;

#nullable disable

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260929090000_AddComplianceV7")]
public partial class AddComplianceV7 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "bcp_code",
            table: "ComplianceRequirement",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "legal_basis",
            table: "ComplianceRequirement",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "applies_per_horse",
            table: "DossierTemplateItem",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "is_deleted",
            table: "DossierRequirement",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "deleted_at",
            table: "DossierRequirement",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "deleted_by_account_id",
            table: "DossierRequirement",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "created_by_account_id",
            table: "ComplianceDocument",
            type: "uuid",
            nullable: false);

        migrationBuilder.AddColumn<Guid>(
            name: "updated_by_account_id",
            table: "ComplianceDocument",
            type: "uuid",
            nullable: false);

        migrationBuilder.AddColumn<Guid>(
            name: "uploaded_by_account_id",
            table: "DocumentVersion",
            type: "uuid",
            nullable: false);

        migrationBuilder.AddColumn<Guid>(
            name: "reviewed_by_account_id",
            table: "DocumentReview",
            type: "uuid",
            nullable: false);

        migrationBuilder.AddColumn<Guid>(
            name: "requested_by_account_id",
            table: "DocumentRequest",
            type: "uuid",
            nullable: false);

        migrationBuilder.AddColumn<Guid>(
            name: "submitted_by_account_id",
            table: "AuthoritySubmission",
            type: "uuid",
            nullable: false);

        migrationBuilder.AddColumn<bool>(
            name: "is_required",
            table: "AuthoritySubmission",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<string>(
            name: "permit_number",
            table: "AuthoritySubmission",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "permit_issued_date",
            table: "AuthoritySubmission",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "permit_expiry_date",
            table: "AuthoritySubmission",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "revision_due_date",
            table: "AuthoritySubmission",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "submitted_note",
            table: "QuarantineProof",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "submitted_note",
            table: "CustomsClearanceProof",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "ata_carnet_expiry_date",
            table: "CustomsClearanceProof",
            type: "date",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "CatalogAuditLog",
            columns: table => new
            {
                catalog_audit_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                action_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                summary = table.Column<string>(type: "text", nullable: true),
                before_data = table.Column<string>(type: "jsonb", nullable: false),
                after_data = table.Column<string>(type: "jsonb", nullable: false),
                reference_count_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("CatalogAuditLog_pkey", x => x.catalog_audit_log_id);
                table.ForeignKey("CatalogAuditLog_actor_account_id_fkey", x => x.actor_account_id, "Account", "account_id");
            });

        migrationBuilder.CreateIndex("DossierRequirement_deleted_by_account_id_idx", "DossierRequirement", "deleted_by_account_id");
        migrationBuilder.CreateIndex("ComplianceDocument_created_by_account_id_idx", "ComplianceDocument", "created_by_account_id");
        migrationBuilder.CreateIndex("ComplianceDocument_updated_by_account_id_idx", "ComplianceDocument", "updated_by_account_id");
        migrationBuilder.CreateIndex("DocumentVersion_uploaded_by_account_id_idx", "DocumentVersion", "uploaded_by_account_id");
        migrationBuilder.CreateIndex("DocumentReview_reviewed_by_account_id_idx", "DocumentReview", "reviewed_by_account_id");
        migrationBuilder.CreateIndex("DocumentRequest_requested_by_account_id_idx", "DocumentRequest", "requested_by_account_id");
        migrationBuilder.CreateIndex("AuthoritySubmission_submitted_by_account_id_idx", "AuthoritySubmission", "submitted_by_account_id");
        migrationBuilder.CreateIndex("CatalogAuditLog_entity_type_entity_id_idx", "CatalogAuditLog", new[] { "entity_type", "entity_id" });
        migrationBuilder.CreateIndex("CatalogAuditLog_actor_account_id_idx", "CatalogAuditLog", "actor_account_id");
        migrationBuilder.CreateIndex("CatalogAuditLog_created_at_idx", "CatalogAuditLog", "created_at");

        migrationBuilder.AddForeignKey("DossierRequirement_deleted_by_account_id_fkey", "DossierRequirement", "deleted_by_account_id", "Account", "account_id");
        migrationBuilder.AddForeignKey("ComplianceDocument_created_by_account_id_fkey", "ComplianceDocument", "created_by_account_id", "Account", "account_id");
        migrationBuilder.AddForeignKey("ComplianceDocument_updated_by_account_id_fkey", "ComplianceDocument", "updated_by_account_id", "Account", "account_id");
        migrationBuilder.AddForeignKey("DocumentVersion_uploaded_by_account_id_fkey", "DocumentVersion", "uploaded_by_account_id", "Account", "account_id");
        migrationBuilder.AddForeignKey("DocumentReview_reviewed_by_account_id_fkey", "DocumentReview", "reviewed_by_account_id", "Account", "account_id");
        migrationBuilder.AddForeignKey("DocumentRequest_requested_by_account_id_fkey", "DocumentRequest", "requested_by_account_id", "Account", "account_id");
        migrationBuilder.AddForeignKey("AuthoritySubmission_submitted_by_account_id_fkey", "AuthoritySubmission", "submitted_by_account_id", "Account", "account_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("DossierRequirement_deleted_by_account_id_fkey", "DossierRequirement");
        migrationBuilder.DropForeignKey("ComplianceDocument_created_by_account_id_fkey", "ComplianceDocument");
        migrationBuilder.DropForeignKey("ComplianceDocument_updated_by_account_id_fkey", "ComplianceDocument");
        migrationBuilder.DropForeignKey("DocumentVersion_uploaded_by_account_id_fkey", "DocumentVersion");
        migrationBuilder.DropForeignKey("DocumentReview_reviewed_by_account_id_fkey", "DocumentReview");
        migrationBuilder.DropForeignKey("DocumentRequest_requested_by_account_id_fkey", "DocumentRequest");
        migrationBuilder.DropForeignKey("AuthoritySubmission_submitted_by_account_id_fkey", "AuthoritySubmission");

        migrationBuilder.DropTable("CatalogAuditLog");

        migrationBuilder.DropColumn("bcp_code", "ComplianceRequirement");
        migrationBuilder.DropColumn("legal_basis", "ComplianceRequirement");
        migrationBuilder.DropColumn("applies_per_horse", "DossierTemplateItem");
        migrationBuilder.DropColumn("is_deleted", "DossierRequirement");
        migrationBuilder.DropColumn("deleted_at", "DossierRequirement");
        migrationBuilder.DropColumn("deleted_by_account_id", "DossierRequirement");
        migrationBuilder.DropColumn("created_by_account_id", "ComplianceDocument");
        migrationBuilder.DropColumn("updated_by_account_id", "ComplianceDocument");
        migrationBuilder.DropColumn("uploaded_by_account_id", "DocumentVersion");
        migrationBuilder.DropColumn("reviewed_by_account_id", "DocumentReview");
        migrationBuilder.DropColumn("requested_by_account_id", "DocumentRequest");
        migrationBuilder.DropColumn("submitted_by_account_id", "AuthoritySubmission");
        migrationBuilder.DropColumn("is_required", "AuthoritySubmission");
        migrationBuilder.DropColumn("permit_number", "AuthoritySubmission");
        migrationBuilder.DropColumn("permit_issued_date", "AuthoritySubmission");
        migrationBuilder.DropColumn("permit_expiry_date", "AuthoritySubmission");
        migrationBuilder.DropColumn("revision_due_date", "AuthoritySubmission");
        migrationBuilder.DropColumn("submitted_note", "QuarantineProof");
        migrationBuilder.DropColumn("submitted_note", "CustomsClearanceProof");
        migrationBuilder.DropColumn("ata_carnet_expiry_date", "CustomsClearanceProof");
    }
}
