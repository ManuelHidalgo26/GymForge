using FluentAssertions;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Seed;
using GymForge.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymForge.Integration.Tests;

/// <summary>
/// Smoke test del arranque real: crea el schema y corre el seeder, igual que
/// InitialiseDatabaseAsync en el arranque del Desktop. Sustituye la verificación
/// visual de la app (que no se puede automatizar en una app de escritorio nativa).
/// </summary>
public class StartupSeedTests
{
    private static async Task<GymForgeDbContext> NewInMemoryDbAsync()
    {
        var opts = new DbContextOptionsBuilder<GymForgeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var db = new GymForgeDbContext(opts);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    [Fact]
    public async Task Seed_WithoutDemoFlag_SeedsCatalogButLeavesTenantEmpty()
    {
        Environment.SetEnvironmentVariable("GYMFORGE_SEED_COMPANY", null);

        await using var db = await NewInMemoryDbAsync();
        var seeder = new DatabaseSeeder(db, NullLogger<DatabaseSeeder>.Instance, new Pbkdf2PinHasher());
        await seeder.SeedAsync();

        // Producción: la biblioteca global se siembra, pero no hay tenant → onboarding.
        (await db.Exercises.CountAsync()).Should().BeGreaterThan(0);
        (await db.Companies.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Seed_WithDemoFlag_PopulatesDefaultTenantAndCatalog_Idempotently()
    {
        Environment.SetEnvironmentVariable("GYMFORGE_SEED_COMPANY", "1");
        try
        {
            await using var db = await NewInMemoryDbAsync();
            var seeder = new DatabaseSeeder(db, NullLogger<DatabaseSeeder>.Instance, new Pbkdf2PinHasher());
            await seeder.SeedAsync();
            // Idempotencia: correrlo dos veces no duplica.
            await seeder.SeedAsync();

            (await db.Companies.CountAsync()).Should().Be(1);
            (await db.Sites.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
            (await db.Staff.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
            (await db.MembershipTypes.CountAsync()).Should().Be(5);
            (await db.Exercises.CountAsync()).Should().BeGreaterThan(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GYMFORGE_SEED_COMPANY", null);
        }
    }
}
