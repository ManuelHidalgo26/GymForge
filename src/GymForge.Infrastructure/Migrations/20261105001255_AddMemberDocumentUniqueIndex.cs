using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberDocumentUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Members_Document",
                table: "Members");

            migrationBuilder.CreateIndex(
                name: "UX_Members_CompanySiteDocument",
                table: "Members",
                columns: new[] { "CompanyId", "SiteId", "DocumentNumber" },
                unique: true,
                filter: "\"DocumentNumber\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Members_CompanySiteDocument",
                table: "Members");

            migrationBuilder.CreateIndex(
                name: "IX_Members_Document",
                table: "Members",
                column: "DocumentNumber");
        }
    }
}
