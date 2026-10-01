using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Migration("20260930123000_RemoveWooCommerce")]
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AgroTradeDbContext))]
    public partial class RemoveWooCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_Products_WooCommerceId";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Products"
                DROP COLUMN IF EXISTS "WooCommerceId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "WooCommerceId",
                table: "Products",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_WooCommerceId",
                table: "Products",
                column: "WooCommerceId",
                unique: true,
                filter: "\"WooCommerceId\" IS NOT NULL");
        }
    }
}
