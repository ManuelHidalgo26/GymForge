using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GymForge.Integration.Tests;

/// <summary>
/// Aplica las migraciones reales (lo que hace el arranque de la app) en vez de EnsureCreated,
/// para detectar un schema migrado distinto del modelo.
/// </summary>
public class MigrationsIntegrationTests
{
    private const string MigrationBeforeFreezePolicy = "20261027171441_AccessLogAllowUnknownMember";

    private static async Task<GymForgeDbContext> NewDbAsync()
    {
        var options = new DbContextOptionsBuilder<GymForgeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var db = new GymForgeDbContext(options, new AmbientTenantProvider());
        await db.Database.OpenConnectionAsync();
        return db;
    }

    private static async Task<GymForgeDbContext> NewMigratedDbAsync()
    {
        var db = await NewDbAsync();
        await db.Database.MigrateAsync();
        return db;
    }

    [Fact]
    public async Task Migrate_LeavesNoPendingModelChanges()
    {
        await using var db = await NewMigratedDbAsync();

        db.Database.HasPendingModelChanges().Should().BeFalse(
            "el snapshot de migraciones debe reflejar el modelo; si no, MigrateAsync falla en el arranque");
    }

    [Fact]
    public async Task Migrate_AccessLogsAcceptSwipesWithoutMember()
    {
        await using var db = await NewMigratedDbAsync();
        var log = AccessLog.DeniedUnknown(
            Guid.NewGuid(), Guid.NewGuid(), 1, AccessMethod.RfidCard, AccessDenialReason.TagUnknown, "TAG-404");

        await db.AccessLogs.AddAsync(log);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var persisted = await db.AccessLogs.IgnoreQueryFilters().SingleAsync();
        persisted.MemberId.Should().BeNull();
    }

    [Fact]
    public async Task Migrate_CompaniesCreatedBeforeTheFreezePolicyGetTheDefaultCap()
    {
        await using var db = await NewDbAsync();
        await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeFreezePolicy);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Companies (Id, LegalName, TaxId, PrimaryLanguage, Currency, Timezone, BrandColorHex, IsActive, CreatedAt, UpdatedAt)
            VALUES ('11111111-1111-1111-1111-111111111111', 'Gym Previo', '30-11111111-1', 'es-AR', 'ARS',
                    'Argentina Standard Time', '#6366F1', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00')
            """);

        await db.Database.MigrateAsync();

        var cap = await db.Companies.Select(c => c.MaxFreezeDaysPerYear).SingleAsync();
        cap.Should().Be(90, "un gimnasio ya instalado conserva la política de 90 días, no queda en 0");
    }
}
