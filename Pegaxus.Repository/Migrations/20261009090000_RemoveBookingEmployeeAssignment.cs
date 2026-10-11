using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261009090000_RemoveBookingEmployeeAssignment")]
public partial class RemoveBookingEmployeeAssignment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("BookingEmployeeAssignment");
        migrationBuilder.Sql("DROP TYPE booking_assignment_status;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE TYPE booking_assignment_status AS ENUM ('ACTIVE', 'REVOKED');");
        migrationBuilder.CreateTable("BookingEmployeeAssignment", columns: table => new
        {
            booking_employee_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
            booking_id = table.Column<Guid>(type: "uuid", nullable: false),
            employee_id = table.Column<Guid>(type: "uuid", nullable: false),
            assignment_role = table.Column<string>(type: "employee_role", nullable: false),
            assigned_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
            status = table.Column<string>(type: "booking_assignment_status", nullable: false, defaultValue: "ACTIVE"),
            assigned_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
            revoked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
            note = table.Column<string>(type: "text", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("BookingEmployeeAssignment_pkey", item => item.booking_employee_assignment_id);
            table.ForeignKey("BookingEmployeeAssignment_booking_id_fkey", item => item.booking_id,
                "Booking", "booking_id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("BookingEmployeeAssignment_employee_id_fkey", item => item.employee_id,
                "Employee", "employee_id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("BookingEmployeeAssignment_assigned_by_employee_id_fkey", item => item.assigned_by_employee_id,
                "Employee", "employee_id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateIndex("BookingEmployeeAssignment_active_role_idx", "BookingEmployeeAssignment",
            new[] { "booking_id", "assignment_role" }, unique: true, filter: "status = 'ACTIVE'");
        migrationBuilder.CreateIndex("BookingEmployeeAssignment_employee_id_idx", "BookingEmployeeAssignment", "employee_id");
        migrationBuilder.CreateIndex("BookingEmployeeAssignment_assigned_by_employee_id_idx", "BookingEmployeeAssignment", "assigned_by_employee_id");
        migrationBuilder.CreateIndex("BookingEmployeeAssignment_status_idx", "BookingEmployeeAssignment", "status");
    }
}
