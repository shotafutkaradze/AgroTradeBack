using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Migration("20260930124000_RemoveProductImportFields")]
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AgroTradeDbContext))]
    public partial class RemoveProductImportFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Products"
                DROP COLUMN IF EXISTS "RawJson";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Products"
                DROP COLUMN IF EXISTS "Source";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Products"
                DROP COLUMN IF EXISTS "SyncedAt";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RawJson",
                table: "Products",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Products",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "local");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncedAt",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
