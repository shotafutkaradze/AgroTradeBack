using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Migration("20260929130000_OrderUsers")]
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AgroTradeDbContext))]
    public partial class OrderUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Orders"
                ADD COLUMN IF NOT EXISTS "AppUserId" integer NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Orders_AppUserId"
                ON "Orders" ("AppUserId");
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'FK_Orders_AppUsers_AppUserId'
                    ) THEN
                        ALTER TABLE "Orders"
                        ADD CONSTRAINT "FK_Orders_AppUsers_AppUserId"
                        FOREIGN KEY ("AppUserId") REFERENCES "AppUsers" ("Id")
                        ON DELETE SET NULL;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_AppUsers_AppUserId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_AppUserId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AppUserId",
                table: "Orders");
        }
    }
}
