using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Migrations
{
    [Migration("20261005100000_CategoryImageUrl")]
    public partial class CategoryImageUrl : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Categories"
                ADD COLUMN IF NOT EXISTS "ImageUrl" character varying(1000) NULL;
            """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Categories");
        }
    }
}
