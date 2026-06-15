using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPoC.Migrations.Mssql
{
    /// <inheritdoc />
    public partial class Addtest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Test",
                table: "WidgetsNewTo",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Test",
                table: "WidgetsNewTo");
        }
    }
}
