using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261005100000_AddTransactionalOutbox")]
public partial class AddTransactionalOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("OutboxMessage", columns: table => new
        {
            outbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
            message_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            payload = table.Column<string>(type: "text", nullable: false),
            status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
            attempts = table.Column<int>(type: "integer", nullable: false),
            available_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
            locked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
            processed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
            last_error = table.Column<string>(type: "text", nullable: true),
            created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
            updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("OutboxMessage_pkey", item => item.outbox_message_id));

        migrationBuilder.CreateIndex("OutboxMessage_pending_idx", "OutboxMessage", new[] { "status", "available_at" });
        migrationBuilder.CreateIndex("OutboxMessage_created_at_idx", "OutboxMessage", "created_at");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("OutboxMessage");
}
