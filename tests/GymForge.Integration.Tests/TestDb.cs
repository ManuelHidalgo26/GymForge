using GymForge.Application.Interfaces;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// Base SQLite en memoria con el schema creado y un tenant mutable para los query filters.
/// Sembrar (insertar) no necesita tenant; para leer hay que fijarlo con <c>tenant.Set(...)</c>
/// o abrir <c>tenant.BeginScope(...)</c>. El llamador es dueño del contexto y de su conexión.
/// Por defecto NO engancha el interceptor de auditoría: <see cref="CreateAudited"/> lo suma.
/// </summary>
internal static class TestDb
{
    public static GymForgeDbContext Create(out AmbientTenantProvider tenant)
    {
        tenant = new AmbientTenantProvider();
        return Build(tenant, new DbContextOptionsBuilder<GymForgeDbContext>());
    }

    /// <summary>Igual que <see cref="Create"/> pero con el AuditSaveChangesInterceptor enganchado.</summary>
    public static GymForgeDbContext CreateAudited(
        out AmbientTenantProvider tenant, ICurrentUser user, IClock clock)
    {
        tenant = new AmbientTenantProvider();
        var builder = new DbContextOptionsBuilder<GymForgeDbContext>()
            .AddInterceptors(new AuditSaveChangesInterceptor(user, clock, tenant));
        return Build(tenant, builder);
    }

    private static GymForgeDbContext Build(
        AmbientTenantProvider tenant, DbContextOptionsBuilder<GymForgeDbContext> builder)
    {
        var db = new GymForgeDbContext(builder.UseSqlite("Data Source=:memory:").Options, tenant);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
