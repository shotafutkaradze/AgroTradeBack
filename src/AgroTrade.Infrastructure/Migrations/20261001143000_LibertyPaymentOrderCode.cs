using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Migration("20261001143000_LibertyPaymentOrderCode")]
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AgroTradeDbContext))]
    public partial class LibertyPaymentOrderCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Orders"
                ADD COLUMN IF NOT EXISTS "LibertyPaymentOrderCode" character varying(100) NULL;
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_name = 'Orders'
                          AND column_name = 'LibertyPayOrderId'
                    ) THEN
                        UPDATE "Orders"
                        SET "LibertyPaymentOrderCode" = "LibertyPayOrderId"
                        WHERE "LibertyPaymentOrderCode" IS NULL;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LibertyPaymentOrderCode",
                table: "Orders");
        }
    }
}
