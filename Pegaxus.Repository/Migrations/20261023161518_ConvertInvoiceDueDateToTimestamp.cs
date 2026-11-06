using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pegaxus.Repository.Migrations
{
    /// <inheritdoc />
    public partial class ConvertInvoiceDueDateToTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A date due meant the whole day, so an existing value becomes the last second of that day (UTC) instead of its midnight.
            migrationBuilder.Sql("ALTER TABLE \"Invoice\" ALTER COLUMN due_date TYPE timestamp without time zone USING (due_date + time '23:59:59');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Invoice\" ALTER COLUMN due_date TYPE date USING due_date::date;");
        }
    }
}
