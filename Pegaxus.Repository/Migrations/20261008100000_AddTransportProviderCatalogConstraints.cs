using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261008100000_AddTransportProviderCatalogConstraints")]
public partial class AddTransportProviderCatalogConstraints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("TransportProvider_provider_code_key", "TransportProvider");
        migrationBuilder.CreateIndex("TransportProvider_provider_code_key", "TransportProvider", "provider_code", unique: true, filter: "is_deleted = false");
        migrationBuilder.CreateIndex("TransportProvider_tax_code_key", "TransportProvider", "tax_code", unique: true, filter: "is_deleted = false");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("TransportProvider_tax_code_key", "TransportProvider");
        migrationBuilder.DropIndex("TransportProvider_provider_code_key", "TransportProvider");
        migrationBuilder.CreateIndex("TransportProvider_provider_code_key", "TransportProvider", "provider_code", unique: true);
    }
}
