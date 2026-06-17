using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantPoC.Migrations.Pgsql
{
    /// <inheritdoc />
    public partial class AddMissingColumns : Migration
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
                name: "NameAr",
                table: "WidgetsNewTo",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameEn",
                table: "WidgetsNewTo",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Test",
                table: "WidgetsNewTo",
                type: "text",
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
                name: "NameAr",
                table: "WidgetsNewTo");

            migrationBuilder.DropColumn(
                name: "NameEn",
                table: "WidgetsNewTo");

            migrationBuilder.DropColumn(
                name: "Test",
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
