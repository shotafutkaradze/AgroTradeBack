
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using AgroTrade.Infrastructure.Data;

#nullable disable

namespace AgroTrade.Infrastructure.Migrations
{
    [DbContext(typeof(AgroTradeDbContext))]
    [Migration("20261005090000_AppUserEmailVerification")]
    public partial class AppUserEmailVerification : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "AppUsers"
                ADD COLUMN IF NOT EXISTS "IsEmailVerified" boolean NOT NULL DEFAULT FALSE;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE "AppUsers"
                ADD COLUMN IF NOT EXISTS "EmailVerificationToken" character varying(120) NULL;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE "AppUsers"
                ADD COLUMN IF NOT EXISTS "EmailVerificationTokenExpiresAt" timestamp with time zone NULL;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE "AppUsers"
                ADD COLUMN IF NOT EXISTS "EmailVerifiedAt" timestamp with time zone NULL;
            """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_AppUsers_EmailVerificationToken"
                ON "AppUsers" ("EmailVerificationToken");
            """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_EmailVerificationToken",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "EmailVerificationToken",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "EmailVerificationTokenExpiresAt",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "IsEmailVerified",
                table: "AppUsers");
        }
    }
}
