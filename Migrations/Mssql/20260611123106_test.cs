using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPoC.Migrations.Mssql
{
    /// <inheritdoc />
    public partial class test : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_WidgetsNewToTest",
                table: "WidgetsNewToTest");

            migrationBuilder.RenameTable(
                name: "WidgetsNewToTest",
                newName: "WidgetsNewTo");

            migrationBuilder.AddColumn<string>(
                name: "NameEn",
                table: "WidgetsNewTo",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WidgetsNewTo",
                table: "WidgetsNewTo",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_WidgetsNewTo",
                table: "WidgetsNewTo");

            migrationBuilder.DropColumn(
                name: "NameEn",
                table: "WidgetsNewTo");

            migrationBuilder.RenameTable(
                name: "WidgetsNewTo",
                newName: "WidgetsNewToTest");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WidgetsNewToTest",
                table: "WidgetsNewToTest",
                column: "Id");
        }
    }
}
