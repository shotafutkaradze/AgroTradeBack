using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Migrations
{
    [Migration("20261005093000_AppUserAddress")]
    public partial class AppUserAddress : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "AppUsers"
                ADD COLUMN IF NOT EXISTS "City" character varying(120) NULL;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE "AppUsers"
                ADD COLUMN IF NOT EXISTS "Region" character varying(160) NULL;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE "AppUsers"
                ADD COLUMN IF NOT EXISTS "Street" character varying(180) NULL;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE "AppUsers"
                ADD COLUMN IF NOT EXISTS "Building" character varying(120) NULL;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE "AppUsers"
                ADD COLUMN IF NOT EXISTS "AddressNote" character varying(300) NULL;
            """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AddressNote", table: "AppUsers");
            migrationBuilder.DropColumn(name: "Building", table: "AppUsers");
            migrationBuilder.DropColumn(name: "Street", table: "AppUsers");
            migrationBuilder.DropColumn(name: "Region", table: "AppUsers");
            migrationBuilder.DropColumn(name: "City", table: "AppUsers");
        }
    }
}
