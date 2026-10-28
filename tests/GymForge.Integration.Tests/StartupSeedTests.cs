using FluentAssertions;
using GymForge.Application;
using GymForge.Application.Interfaces;
using GymForge.Infrastructure;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Seed;
using GymForge.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymForge.Integration.Tests;

/// <summary>
/// Smoke test del arranque real: crea el schema y corre el seeder, igual que
/// InitialiseDatabaseAsync en el arranque del Desktop. Sustituye la verificación
/// visual de la app (que no se puede automatizar en una app de escritorio nativa).
/// </summary>
public class StartupSeedTests
{
    [Fact]
    public async Task Seed_WithoutDemoFlag_SeedsCatalogButLeavesTenantEmpty()
    {
        Environment.SetEnvironmentVariable("GYMFORGE_SEED_COMPANY", null);

        await using var db = TestDb.Create(out _);
        var seeder = new DatabaseSeeder(db, NullLogger<DatabaseSeeder>.Instance, new Pbkdf2PinHasher());
        await seeder.SeedAsync();

        // Producción: la biblioteca global se siembra, pero no hay tenant → onboarding.
        (await db.Exercises.CountAsync()).Should().BeGreaterThan(0);
        (await db.Companies.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InitialiseDatabase_WithDemoData_SeedsEverythingBeforeAnyTenantIsSet()
    {
        Environment.SetEnvironmentVariable("GYMFORGE_SEED_COMPANY", "1");
        Environment.SetEnvironmentVariable("GYMFORGE_SEED_DEMO", "1");
        var dir = Path.Combine(Path.GetTempPath(), "gymforge_startup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // Mismo arranque que el escritorio: migrar + sembrar con el tenant todavía vacío.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddApplication();
            services.AddSingleton<ICurrentTenant, AmbientTenantProvider>();
            services.AddSingleton<ICurrentUser, AmbientCurrentUser>();
            services.AddInfrastructure($"Data Source={Path.Combine(dir, "gymforge.db")}");
            await using var provider = services.BuildServiceProvider();

            await provider.InitialiseDatabaseAsync();
            // Idempotente: un segundo arranque no duplica el demo.
            await provider.InitialiseDatabaseAsync();

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GymForgeDbContext>();
            (await db.Members.IgnoreQueryFilters().CountAsync()).Should().Be(2);
            (await db.Memberships.IgnoreQueryFilters().CountAsync()).Should().Be(2);
            (await db.ClassSchedules.IgnoreQueryFilters().CountAsync()).Should().Be(1);
            (await db.Routines.IgnoreQueryFilters().CountAsync()).Should().Be(1);

            // El interceptor de auditoría quedó enganchado por DI: el seed dejó rastro, sin usuario (sistema).
            var seededAudit = await db.AuditLogs.IgnoreQueryFilters().ToListAsync();
            seededAudit.Should().Contain(a => a.EntityType == "Site" && a.Action == "Insert");
            seededAudit.Should().OnlyContain(a => a.UserId == null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GYMFORGE_SEED_COMPANY", null);
            Environment.SetEnvironmentVariable("GYMFORGE_SEED_DEMO", null);
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Seed_WithDemoFlag_PopulatesDefaultTenantAndCatalog_Idempotently()
    {
        Environment.SetEnvironmentVariable("GYMFORGE_SEED_COMPANY", "1");
        try
        {
            // El seed corre en el arranque, antes de conocer el tenant: no hay tenant fijado.
            await using var db = TestDb.Create(out _);
            var seeder = new DatabaseSeeder(db, NullLogger<DatabaseSeeder>.Instance, new Pbkdf2PinHasher());
            await seeder.SeedAsync();
            // Idempotencia: correrlo dos veces no duplica.
            await seeder.SeedAsync();

            (await db.Companies.CountAsync()).Should().Be(1);
            (await db.Sites.IgnoreQueryFilters().CountAsync()).Should().BeGreaterThanOrEqualTo(1);
            (await db.Staff.IgnoreQueryFilters().CountAsync()).Should().BeGreaterThanOrEqualTo(1);
            (await db.MembershipTypes.IgnoreQueryFilters().CountAsync()).Should().Be(5);
            (await db.Exercises.IgnoreQueryFilters().CountAsync()).Should().BeGreaterThan(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GYMFORGE_SEED_COMPANY", null);
        }
    }
}
