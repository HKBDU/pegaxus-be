using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261003000000_AddDocumentRequestRevisions")]
public partial class AddDocumentRequestRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("document_review_id", "DocumentRequest", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>("error_category", "DocumentRequest", type: "character varying(40)", maxLength: 40, nullable: true);
        migrationBuilder.CreateIndex("DocumentRequest_document_review_id_key", "DocumentRequest", "document_review_id", unique: true);
        migrationBuilder.AddForeignKey("DocumentRequest_document_review_id_fkey", "DocumentRequest", "document_review_id",
            "DocumentReview", principalColumn: "document_review_id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateTable("DocumentRequestComment", columns: table => new
        {
            document_request_comment_id = table.Column<Guid>(type: "uuid", nullable: false),
            document_request_id = table.Column<Guid>(type: "uuid", nullable: false),
            author_account_id = table.Column<Guid>(type: "uuid", nullable: false),
            message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
            created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("DocumentRequestComment_pkey", item => item.document_request_comment_id);
            table.ForeignKey("DocumentRequestComment_document_request_id_fkey", item => item.document_request_id,
                "DocumentRequest", "document_request_id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("DocumentRequestComment_author_account_id_fkey", item => item.author_account_id,
                "Account", "account_id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("DocumentRequestComment_request_created_idx", "DocumentRequestComment", new[] { "document_request_id", "created_at" });
        migrationBuilder.CreateIndex("DocumentRequestComment_author_account_id_idx", "DocumentRequestComment", "author_account_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("DocumentRequestComment");
        migrationBuilder.DropForeignKey("DocumentRequest_document_review_id_fkey", "DocumentRequest");
        migrationBuilder.DropIndex("DocumentRequest_document_review_id_key", "DocumentRequest");
        migrationBuilder.DropColumn("document_review_id", "DocumentRequest");
        migrationBuilder.DropColumn("error_category", "DocumentRequest");
    }
}
