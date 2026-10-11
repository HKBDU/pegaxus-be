using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261011110000_AddInvoiceType")]
public partial class AddInvoiceType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:Enum:invoice_type", "QUOTATION,DEPOSIT,FINAL,SUPPLEMENTARY,CREDIT_NOTE");

        migrationBuilder.AddColumn<InvoiceType>(
            name: "type",
            table: "Invoice",
            type: "invoice_type",
            nullable: false,
            defaultValueSql: "'QUOTATION'::invoice_type");

        migrationBuilder.CreateIndex(
            name: "Invoice_type_idx",
            table: "Invoice",
            column: "type");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "Invoice_type_idx",
            table: "Invoice");

        migrationBuilder.DropColumn(
            name: "type",
            table: "Invoice");

        migrationBuilder.AlterDatabase()
            .OldAnnotation("Npgsql:Enum:invoice_type", "QUOTATION,DEPOSIT,FINAL,SUPPLEMENTARY,CREDIT_NOTE");
    }
}
