using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pegaxus.Repository.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261016120000_AddHorseHealthLogPhotos")]
public partial class AddHorseHealthLogPhotos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(name: "photo_url", table: "HorseHealthLog", newName: "photo_urls");
        // Convert in place so every existing evidence URL survives the schema change.
        migrationBuilder.Sql("""
            ALTER TABLE "HorseHealthLog"
                ALTER COLUMN photo_urls TYPE character varying(500)[]
                USING CASE WHEN photo_urls IS NULL THEN ARRAY[]::character varying(500)[] ELSE ARRAY[photo_urls] END,
                ALTER COLUMN photo_urls SET DEFAULT ARRAY[]::character varying(500)[],
                ALTER COLUMN photo_urls SET NOT NULL;
            """);
        migrationBuilder.AddCheckConstraint(name: "HorseHealthLog_photos_check", table: "HorseHealthLog",
            sql: "cardinality(photo_urls) <= 5 AND array_position(photo_urls, NULL) IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A scalar cannot preserve multiple photos; refuse rollback rather than discard evidence.
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM "HorseHealthLog" WHERE cardinality(photo_urls) > 1) THEN
                    RAISE EXCEPTION 'Cannot roll back AddHorseHealthLogPhotos while health logs have multiple photos.';
                END IF;
            END $$;
            """);
        migrationBuilder.DropCheckConstraint(name: "HorseHealthLog_photos_check", table: "HorseHealthLog");
        migrationBuilder.Sql("""
            ALTER TABLE "HorseHealthLog"
                ALTER COLUMN photo_urls DROP DEFAULT,
                ALTER COLUMN photo_urls DROP NOT NULL,
                ALTER COLUMN photo_urls TYPE character varying(500) USING photo_urls[1];
            """);
        migrationBuilder.RenameColumn(name: "photo_urls", table: "HorseHealthLog", newName: "photo_url");
    }
}
