using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteIdToRoutineWorkoutBodyBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "WorkoutLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Routines",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Bookings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "BodyMeasurements",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill: las filas existentes heredan la sede de su padre (socio u horario) ANTES de que el
            // filtro global por sede empiece a exigir el match. Las columnas nacen con Guid.Empty.
            foreach (var table in new[] { "Routines", "WorkoutLogs", "BodyMeasurements" })
            {
                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"SiteId\" = " +
                    "(SELECT m.\"SiteId\" FROM \"Members\" m WHERE m.\"Id\" = \"" + table + "\".\"MemberId\");");
            }

            migrationBuilder.Sql(
                "UPDATE \"Bookings\" SET \"SiteId\" = " +
                "(SELECT cs.\"SiteId\" FROM \"ClassSchedules\" cs WHERE cs.\"Id\" = \"Bookings\".\"ClassScheduleId\");");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutLogs_CompanySite",
                table: "WorkoutLogs",
                columns: new[] { "CompanyId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Routines_CompanySite",
                table: "Routines",
                columns: new[] { "CompanyId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CompanySite",
                table: "Bookings",
                columns: new[] { "CompanyId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_BodyMeasurements_CompanySite",
                table: "BodyMeasurements",
                columns: new[] { "CompanyId", "SiteId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkoutLogs_CompanySite",
                table: "WorkoutLogs");

            migrationBuilder.DropIndex(
                name: "IX_Routines_CompanySite",
                table: "Routines");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_CompanySite",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_BodyMeasurements_CompanySite",
                table: "BodyMeasurements");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "WorkoutLogs");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Routines");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "BodyMeasurements");
        }
    }
}
