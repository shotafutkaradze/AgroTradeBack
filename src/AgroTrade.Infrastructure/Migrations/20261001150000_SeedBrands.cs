using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Migrations
{
    public partial class SeedBrands : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "Brands" ("Name", "Slug", "SortOrder", "CreatedAt", "UpdatedAt")
                VALUES
                    ('STIHL', 'stihl', 10, NOW(), NOW()),
                    ('BUFFALO', 'buffalo', 20, NOW(), NOW()),
                    ('HONDA', 'honda', 30, NOW(), NOW()),
                    ('KÄRCHER', 'karcher', 40, NOW(), NOW()),
                    ('TOTAL', 'total', 50, NOW(), NOW()),
                    ('INGCO', 'ingco', 60, NOW(), NOW())
                ON CONFLICT ("Slug") DO NOTHING;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "Brands"
                WHERE "Slug" IN ('stihl', 'buffalo', 'honda', 'karcher', 'total', 'ingco')
                  AND NOT EXISTS (
                      SELECT 1 FROM "Products" WHERE "Products"."BrandId" = "Brands"."Id"
                  );
                """);
        }
    }
}
