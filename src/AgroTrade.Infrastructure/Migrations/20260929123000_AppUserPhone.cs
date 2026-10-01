using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Migration("20260929123000_AppUserPhone")]
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AgroTradeDbContext))]
    public partial class AppUserPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
