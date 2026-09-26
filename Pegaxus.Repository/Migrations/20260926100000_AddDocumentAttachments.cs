using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pegaxus.Repository;

#nullable disable

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260926100000_AddDocumentAttachments")]
public partial class AddDocumentAttachments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DocumentAttachment",
            columns: table => new
            {
                document_attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                file_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                sort_order = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("DocumentAttachment_pkey", x => x.document_attachment_id);
                table.ForeignKey(
                    name: "DocumentAttachment_document_version_id_fkey",
                    column: x => x.document_version_id,
                    principalTable: "DocumentVersion",
                    principalColumn: "document_version_id");
            });

        migrationBuilder.CreateIndex(
            name: "DocumentAttachment_document_version_id_idx",
            table: "DocumentAttachment",
            column: "document_version_id");

        migrationBuilder.CreateIndex(
            name: "DocumentAttachment_document_version_id_sort_order_key",
            table: "DocumentAttachment",
            columns: new[] { "document_version_id", "sort_order" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DocumentAttachment");
    }
}
