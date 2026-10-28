using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFreezePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FreezeDaysUsedYear",
                table: "Memberships",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FreezeYear",
                table: "Memberships",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxFreezeDaysPerYear",
                table: "Companies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 90);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FreezeDaysUsedYear",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "FreezeYear",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "MaxFreezeDaysPerYear",
                table: "Companies");
        }
    }
}
