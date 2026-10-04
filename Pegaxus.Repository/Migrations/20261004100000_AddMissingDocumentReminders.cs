using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261004100000_AddMissingDocumentReminders")]
public partial class AddMissingDocumentReminders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("MissingDocumentReminder", columns: table => new
        {
            missing_document_reminder_id = table.Column<Guid>(type: "uuid", nullable: false),
            compliance_dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
            recipient_account_id = table.Column<Guid>(type: "uuid", nullable: false),
            requirement_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
            due_date = table.Column<DateOnly>(type: "date", nullable: false),
            note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
            automatic_date = table.Column<DateOnly>(type: "date", nullable: true),
            created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
            last_attempt_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
            email_sent_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
            cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
            email_attempts = table.Column<int>(type: "integer", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("MissingDocumentReminder_pkey", item => item.missing_document_reminder_id);
            table.ForeignKey("MissingDocumentReminder_dossier_fkey", item => item.compliance_dossier_id,
                "ComplianceDossier", "compliance_dossier_id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("MissingDocumentReminder_recipient_fkey", item => item.recipient_account_id,
                "Account", "account_id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("MissingDocumentReminder_daily_key", "MissingDocumentReminder", new[] { "compliance_dossier_id", "automatic_date" },
            unique: true, filter: "automatic_date IS NOT NULL");
        migrationBuilder.CreateIndex("MissingDocumentReminder_pending_idx", "MissingDocumentReminder", new[] { "email_sent_at", "cancelled_at", "last_attempt_at" });
        migrationBuilder.CreateIndex("MissingDocumentReminder_recipient_idx", "MissingDocumentReminder", "recipient_account_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("MissingDocumentReminder");
}
