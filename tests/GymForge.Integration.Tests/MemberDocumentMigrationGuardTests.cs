using FluentAssertions;
using GymForge.Application;
using GymForge.Application.Interfaces;
using GymForge.Infrastructure;
using GymForge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GymForge.Integration.Tests;

/// <summary>
/// Chequeo previo a migrar: una base viva con DNIs repetidos por (empresa, sede) no debe llegar al
/// CREATE UNIQUE INDEX de la migración D7 (error críptico de SQLite) sino abortar con un mensaje accionable.
/// </summary>
public class MemberDocumentMigrationGuardTests : IDisposable
{
    private const string MigrationBeforeUniqueIndex = "20261105000953_AddSiteIdToRoutineWorkoutBodyBooking";
    private const string CompanyId = "CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC";
    private const string SiteId = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";
    private const string OtherSiteId = "BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gymforge_guard_" + Guid.NewGuid().ToString("N"));

    public MemberDocumentMigrationGuardTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task InitialiseDatabase_WithDuplicateDocumentsInTheSameSite_AbortsWithActionableMessageAndTouchesNothing()
    {
        await using var provider = BuildProvider();
        await PrepareLegacyDatabaseAsync(provider,
            ("11111111-1111-1111-1111-111111111111", SiteId, "30111222"),
            ("22222222-2222-2222-2222-222222222222", SiteId, "30111222"),
            ("33333333-3333-3333-3333-333333333333", SiteId, "40999888"));

        var act = () => provider.InitialiseDatabaseAsync();

        var error = (await act.Should().ThrowAsync<DuplicateMemberDocumentsBlockMigrationException>()).Which;
        error.Message.Should().Contain("30111222").And.Contain("x2").And.Contain("copia de seguridad")
            .And.Contain("No se modificó ni se borró ningún dato");
        error.Message.Should().NotContain("40999888", "un DNI sin repetir no es parte del problema");

        await using var db = OpenDb(provider);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Members").SingleAsync()).Should().Be(3);
        (await db.Database.GetPendingMigrationsAsync()).Should().Contain("20261105001255_AddMemberDocumentUniqueIndex");
    }

    [Fact]
    public async Task InitialiseDatabase_WithTheSameDocumentInDifferentSites_MigratesNormally()
    {
        await using var provider = BuildProvider();
        await PrepareLegacyDatabaseAsync(provider,
            ("11111111-1111-1111-1111-111111111111", SiteId, "30111222"),
            ("22222222-2222-2222-2222-222222222222", OtherSiteId, "30111222"));

        await provider.InitialiseDatabaseAsync();

        await using var db = OpenDb(provider);
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Members").SingleAsync()).Should().Be(2);
    }

    [Fact]
    public async Task InitialiseDatabase_OnAFreshDatabase_MigratesNormally()
    {
        await using var provider = BuildProvider();

        await provider.InitialiseDatabaseAsync();

        await using var db = OpenDb(provider);
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<ICurrentTenant, AmbientTenantProvider>();
        services.AddSingleton<ICurrentUser, AmbientCurrentUser>();
        services.AddInfrastructure($"Data Source={Path.Combine(_dir, "gymforge.db")}");
        services.AddSingleton<ILocalSettings>(new InMemoryLocalSettings());
        return services.BuildServiceProvider();
    }

    private static GymForgeDbContext OpenDb(ServiceProvider provider) =>
        provider.CreateScope().ServiceProvider.GetRequiredService<GymForgeDbContext>();

    /// <summary>Deja la base como una instalación vieja: migrada hasta justo antes del índice único, con socios cargados.</summary>
    private static async Task PrepareLegacyDatabaseAsync(
        ServiceProvider provider, params (string Id, string SiteId, string Document)[] members)
    {
        await using var db = OpenDb(provider);
        await db.Database.OpenConnectionAsync();
        await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeUniqueIndex);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        foreach (var (id, siteId, document) in members)
            await db.Database.ExecuteSqlRawAsync(
                $"""
                INSERT INTO Members (Id, CompanyId, SiteId, FirstName, LastName, DocumentType, DocumentNumber, Gender,
                                     Status, BloodType, Source, MarketingConsent, CreatedAt, UpdatedAt)
                VALUES ('{id}', '{CompanyId}', '{siteId}', 'Ana', 'Lopez', 'DNI', '{document}', 'Female',
                        'Active', 'Unknown', 'WalkIn', 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00')
                """);
        await db.Database.CloseConnectionAsync();
    }
}
