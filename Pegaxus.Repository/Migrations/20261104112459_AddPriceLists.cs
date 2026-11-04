using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pegaxus.Repository.Enum;

#nullable disable

namespace Pegaxus.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "price_list_item_id",
                table: "InvoiceItem",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_rate",
                table: "Invoice",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 20m);

            migrationBuilder.CreateTable(
                name: "PriceList",
                columns: table => new
                {
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValueSql: "'VND'"),
                    tax_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    deposit_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_account_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PriceList_pkey", x => x.price_list_id);
                    table.CheckConstraint("PriceList_deposit_rate_check", "deposit_rate >= 0 AND deposit_rate <= 100");
                    table.CheckConstraint("PriceList_tax_rate_check", "tax_rate >= 0 AND tax_rate <= 100");
                    table.ForeignKey(
                        name: "PriceList_created_by_account_id_fkey",
                        column: x => x.created_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                    table.ForeignKey(
                        name: "PriceList_updated_by_account_id_fkey",
                        column: x => x.updated_by_account_id,
                        principalTable: "Account",
                        principalColumn: "account_id");
                });

            migrationBuilder.CreateTable(
                name: "PriceListItem",
                columns: table => new
                {
                    price_list_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    item_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    item_type = table.Column<InvoiceItemType>(type: "invoice_item_type", nullable: false),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vehicle_tier = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PriceListItem_pkey", x => x.price_list_item_id);
                    table.CheckConstraint("PriceListItem_unit_price_check", "unit_price > 0");
                    table.CheckConstraint("PriceListItem_vehicle_tier_check", "vehicle_tier IS NULL OR vehicle_tier IN ('S', 'M', 'L')");
                    table.ForeignKey(
                        name: "PriceListItem_price_list_id_fkey",
                        column: x => x.price_list_id,
                        principalTable: "PriceList",
                        principalColumn: "price_list_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceList_created_by_account_id",
                table: "PriceList",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_PriceList_updated_by_account_id",
                table: "PriceList",
                column: "updated_by_account_id");

            migrationBuilder.CreateIndex(
                name: "PriceList_created_at_idx",
                table: "PriceList",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "PriceList_single_active_idx",
                table: "PriceList",
                column: "is_active",
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "PriceListItem_price_list_id_item_code_key",
                table: "PriceListItem",
                columns: new[] { "price_list_id", "item_code" },
                unique: true);

            // Credit notes carry no VAT; every older invoice was taxed at the fixed 20 percent the column defaults to.
            migrationBuilder.Sql("UPDATE \"Invoice\" SET tax_rate = 0 WHERE type = 'CREDIT_NOTE'::invoice_type");

            // The list prices of business/quotation.md as the first active price list, so quotations are unchanged until a manager edits them.
            migrationBuilder.Sql("""
                INSERT INTO "PriceList" (price_list_id, name, currency, tax_rate, deposit_rate, is_active, created_at, updated_at)
                VALUES ('00000000-0000-0000-0000-00000000b001', 'Default price list', 'VND', 20.00, 40.00, true,
                        TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00');
                """);
            migrationBuilder.Sql("""
                INSERT INTO "PriceListItem" (price_list_item_id, price_list_id, item_code, item_name, item_type, unit, unit_price, vehicle_tier, sort_order, created_at, updated_at)
                VALUES
                ('00000000-0000-0000-0000-00000000b101', '00000000-0000-0000-0000-00000000b001', 'TRUCK_S', 'Base vehicle fare, Tier S', 'TRANSPORT_FEE'::invoice_item_type, 'TRIP', 1600, 'S', 10, TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00'),
                ('00000000-0000-0000-0000-00000000b102', '00000000-0000-0000-0000-00000000b001', 'TRUCK_M', 'Base vehicle fare, Tier M', 'TRANSPORT_FEE'::invoice_item_type, 'TRIP', 2400, 'M', 10, TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00'),
                ('00000000-0000-0000-0000-00000000b103', '00000000-0000-0000-0000-00000000b001', 'TRUCK_L', 'Base vehicle fare, Tier L', 'TRANSPORT_FEE'::invoice_item_type, 'TRIP', 3400, 'L', 10, TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00'),
                ('00000000-0000-0000-0000-00000000b104', '00000000-0000-0000-0000-00000000b001', 'HORSE_LOAD', 'Load and compartment surcharge', 'TRANSPORT_FEE'::invoice_item_type, 'HORSE', 120, NULL, 20, TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00'),
                ('00000000-0000-0000-0000-00000000b105', '00000000-0000-0000-0000-00000000b001', 'FEEDING', 'Feed and electrolyte water', 'HORSE_SERVICE_FEE'::invoice_item_type, 'HORSE_DAY', 35, NULL, 30, TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00'),
                ('00000000-0000-0000-0000-00000000b106', '00000000-0000-0000-0000-00000000b001', 'MEDICAL', 'Veterinary care and sterile bedding', 'HORSE_SERVICE_FEE'::invoice_item_type, 'HORSE', 110, NULL, 40, TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00'),
                ('00000000-0000-0000-0000-00000000b107', '00000000-0000-0000-0000-00000000b001', 'HANDLING', 'Hydraulic lift handling and e-POD', 'HANDLING_FEE'::invoice_item_type, 'HORSE', 50, NULL, 50, TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00'),
                ('00000000-0000-0000-0000-00000000b108', '00000000-0000-0000-0000-00000000b001', 'DISINFECTION', 'Vehicle biosecurity disinfection', 'HANDLING_FEE'::invoice_item_type, 'TRIP', 150, NULL, 60, TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00'),
                ('00000000-0000-0000-0000-00000000b109', '00000000-0000-0000-0000-00000000b001', 'COMPLIANCE_FILING', 'TRACES-NT and customs filing', 'COMPLIANCE_FEE'::invoice_item_type, 'TRIP', 250, NULL, 70, TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00'),
                ('00000000-0000-0000-0000-00000000b10a', '00000000-0000-0000-0000-00000000b001', 'CLINICAL_CHECK', 'Clinical exam and microchip check at BCP', 'COMPLIANCE_FEE'::invoice_item_type, 'HORSE', 90, NULL, 80, TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00'),
                ('00000000-0000-0000-0000-00000000b10b', '00000000-0000-0000-0000-00000000b001', 'INSURANCE', 'Equine transit liability insurance', 'OTHER'::invoice_item_type, 'HORSE', 80, NULL, 90, TIMESTAMP '2026-11-04 00:00:00', TIMESTAMP '2026-11-04 00:00:00');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceListItem");

            migrationBuilder.DropTable(
                name: "PriceList");

            migrationBuilder.DropColumn(
                name: "price_list_item_id",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "tax_rate",
                table: "Invoice");
        }
    }
}
