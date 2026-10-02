using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Migrations
{
    public partial class AppUserBlocked : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBlocked",
                table: "AppUsers");
        }
    }
}
