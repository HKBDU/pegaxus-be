using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261002090000_AllowReaddingRemovedDossierRequirements")]
public partial class AllowReaddingRemovedDossierRequirements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("DossierRequirement_dossier_requirement_global_key", "DossierRequirement");
        migrationBuilder.DropIndex("DossierRequirement_dossier_requirement_horse_key", "DossierRequirement");
        migrationBuilder.CreateIndex("DossierRequirement_dossier_requirement_global_key", "DossierRequirement",
            new[] { "compliance_dossier_id", "compliance_requirement_id" }, unique: true,
            filter: "horse_id IS NULL AND is_deleted = false");
        migrationBuilder.CreateIndex("DossierRequirement_dossier_requirement_horse_key", "DossierRequirement",
            new[] { "compliance_dossier_id", "compliance_requirement_id", "horse_id" }, unique: true,
            filter: "horse_id IS NOT NULL AND is_deleted = false");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("DossierRequirement_dossier_requirement_global_key", "DossierRequirement");
        migrationBuilder.DropIndex("DossierRequirement_dossier_requirement_horse_key", "DossierRequirement");
        migrationBuilder.CreateIndex("DossierRequirement_dossier_requirement_global_key", "DossierRequirement",
            new[] { "compliance_dossier_id", "compliance_requirement_id" }, unique: true,
            filter: "horse_id IS NULL");
        migrationBuilder.CreateIndex("DossierRequirement_dossier_requirement_horse_key", "DossierRequirement",
            new[] { "compliance_dossier_id", "compliance_requirement_id", "horse_id" }, unique: true,
            filter: "horse_id IS NOT NULL");
    }
}
