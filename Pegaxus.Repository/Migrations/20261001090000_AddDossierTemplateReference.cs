using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261001090000_AddDossierTemplateReference")]
public partial class AddDossierTemplateReference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "dossier_template_id", table: "ComplianceDossier", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(
            name: "ComplianceDossier_dossier_template_id_idx", table: "ComplianceDossier", column: "dossier_template_id");
        migrationBuilder.AddForeignKey(
            name: "ComplianceDossier_dossier_template_id_fkey", table: "ComplianceDossier", column: "dossier_template_id",
            principalTable: "DossierTemplate", principalColumn: "dossier_template_id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "ComplianceDossier_dossier_template_id_fkey", table: "ComplianceDossier");
        migrationBuilder.DropIndex(name: "ComplianceDossier_dossier_template_id_idx", table: "ComplianceDossier");
        migrationBuilder.DropColumn(name: "dossier_template_id", table: "ComplianceDossier");
    }
}
