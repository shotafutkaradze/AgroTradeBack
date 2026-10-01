using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Migrations
{
    [Migration("20261001110000_PaymentRefunds")]
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AgroTradeDbContext))]
    public partial class PaymentRefunds : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Payments"
                    ADD COLUMN IF NOT EXISTS "RefundedAmount" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS "RefundReason" character varying(500) NULL,
                    ADD COLUMN IF NOT EXISTS "ProviderRefundStatus" character varying(100) NULL,
                    ADD COLUMN IF NOT EXISTS "RefundedAt" timestamp with time zone NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Payments"
                    DROP COLUMN IF EXISTS "RefundedAmount",
                    DROP COLUMN IF EXISTS "RefundReason",
                    DROP COLUMN IF EXISTS "ProviderRefundStatus",
                    DROP COLUMN IF EXISTS "RefundedAt";
                """);
        }
    }
}
