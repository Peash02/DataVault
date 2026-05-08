using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVault.MVC.Migrations
{
    /// <inheritdoc />
    public partial class RenameToIntendedFor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RecipientEmail",
                table: "ShareLinks",
                newName: "IntendedFor");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IntendedFor",
                table: "ShareLinks",
                newName: "RecipientEmail");
        }
    }
}