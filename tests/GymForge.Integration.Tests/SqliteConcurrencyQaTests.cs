using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// Desde el mantenimiento diario hay dos DbContext escribiendo a la vez (la UI y el job en segundo plano). Si el
/// motor devolviera "database is locked" sin reintentar, el mantenimiento o el cobro del cajero fallarían al azar.
/// La configuración de producción del escritorio es WAL sin shared-cache (<c>sharedCache: false</c>, ver NEW-42);
/// la variante con <c>Cache=Shared</c> queda como caso aparte de la config anterior.
/// </summary>
public class SqliteConcurrencyQaTests : IDisposable
{
    private const int Iterations = 150;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"gymforge-qa-{Guid.NewGuid():N}.db");

    private string ConnectionString(bool sharedCache) =>
        $"Data Source={_path};Mode=ReadWriteCreate" + (sharedCache ? ";Cache=Shared" : string.Empty);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); }
        catch (IOException) { /* best-effort: es un archivo temporal */ }
    }

    private GymForgeDbContext Open(AmbientTenantProvider tenant, bool sharedCache) =>
        new(new DbContextOptionsBuilder<GymForgeDbContext>().UseSqlite(ConnectionString(sharedCache)).Options, tenant);

    [Theory]
    [InlineData(false)]   // producción: WAL, sin shared-cache
    [InlineData(true)]    // config anterior: WAL + Cache=Shared
    public async Task UiWriterAndMaintenanceWriter_OnTheSameFile_NeverLockEachOtherOut(bool sharedCache)
    {
        var company = Company.Create("Gym Test", "30-12345678-9");
        var site = Site.Create(company.Id, "Central", "Av. Test 123");
        await using (var seed = Open(new AmbientTenantProvider(), sharedCache))
        {
            await seed.Database.EnsureCreatedAsync();
            await seed.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL");
            await seed.Companies.AddAsync(company);
            await seed.Sites.AddAsync(site);
            await seed.SaveChangesAsync();
        }

        var errors = new List<string>();

        // "UI": un DbContext que inserta socios y lee sin parar.
        var ui = Task.Run(async () =>
        {
            var tenant = new AmbientTenantProvider();
            tenant.Set(company.Id, site.Id);
            await using var db = Open(tenant, sharedCache);
            for (var i = 0; i < Iterations; i++)
            {
                try
                {
                    await db.Members.AddAsync(Member.Create(
                        company.Id, site.Id, "U", $"i{i}", DocumentType.DNI, $"1{i:D7}", Gender.Female));
                    await db.SaveChangesAsync();
                    _ = await db.Members.CountAsync();
                }
                catch (Exception ex) { lock (errors) errors.Add("UI: " + ex.Message); }
            }
        });

        // "Mantenimiento": otro DbContext (otro scope) que lee company-wide y escribe cambios.
        var maintenance = Task.Run(async () =>
        {
            await using var db = Open(new AmbientTenantProvider(), sharedCache);
            for (var i = 0; i < Iterations; i++)
            {
                try
                {
                    var members = await db.Members.IgnoreQueryFilters()
                        .Where(m => m.CompanyId == company.Id).ToListAsync();
                    foreach (var member in members.Take(5))
                        member.Suspend();
                    await db.SaveChangesAsync();
                }
                catch (Exception ex) { lock (errors) errors.Add("MANTENIMIENTO: " + ex.Message); }
            }
        });

        await Task.WhenAll(ui, maintenance);

        errors.Distinct().Should().BeEmpty();
    }
}
