using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261004000000_AddRevokedDocumentReviewResult")]
public partial class AddRevokedDocumentReviewResult : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TYPE document_review_result ADD VALUE IF NOT EXISTS 'REVOKED';", suppressTransaction: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("REVOKED cannot be removed while preserving immutable DocumentReview history.");
}
