using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pegaxus.Repository;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261011100000_AddCustomerCode")]
public partial class AddCustomerCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "customer_code",
            table: "Customer",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);

        migrationBuilder.Sql("""
            WITH numbered AS (
                SELECT customer_id,
                       'CUS-' || to_char(created_at, 'YYYYMMDD') || '-' || lpad(row_number() OVER (ORDER BY customer_id)::text, 4, '0') AS code
                FROM "Customer"
                WHERE customer_code IS NULL
            )
            UPDATE "Customer" AS customer
            SET customer_code = numbered.code
            FROM numbered
            WHERE customer.customer_id = numbered.customer_id;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "customer_code",
            table: "Customer",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "Customer_customer_code_key",
            table: "Customer",
            column: "customer_code",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "Customer_customer_code_key",
            table: "Customer");

        migrationBuilder.DropColumn(
            name: "customer_code",
            table: "Customer");
    }
}
