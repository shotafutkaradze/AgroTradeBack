using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AgroTrade.Infrastructure.Migrations;

[DbContext(typeof(AgroTradeDbContext))]
[Migration("20261109140000_BogPaymentManager")]
public class BogPaymentManager : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("CheckoutIdempotencyKey", "Payments", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>("CheckoutTokenHash", "Payments", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<int>("InstallmentMonth", "Payments", type: "integer", nullable: true);
        migrationBuilder.AddColumn<string>("InstallmentType", "Payments", type: "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("RedirectUrl", "Payments", type: "character varying(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.AddColumn<long>("Version", "Payments", type: "bigint", nullable: false, defaultValue: 0L);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var column in new[] { "CheckoutIdempotencyKey", "CheckoutTokenHash", "InstallmentMonth", "InstallmentType", "RedirectUrl", "Version" })
            migrationBuilder.DropColumn(column, "Payments");
    }
}
