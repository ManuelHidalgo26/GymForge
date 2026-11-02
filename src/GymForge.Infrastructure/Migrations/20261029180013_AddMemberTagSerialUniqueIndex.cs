using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberTagSerialUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Members_TagSerial",
                table: "Members");

            migrationBuilder.CreateIndex(
                name: "UX_Members_CompanyTagSerial",
                table: "Members",
                columns: new[] { "CompanyId", "TagSerial" },
                unique: true,
                filter: "\"TagSerial\" IS NOT NULL AND \"TagSerial\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Members_CompanyTagSerial",
                table: "Members");

            migrationBuilder.CreateIndex(
                name: "IX_Members_TagSerial",
                table: "Members",
                column: "TagSerial");
        }
    }
}
