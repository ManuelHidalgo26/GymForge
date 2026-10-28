using GymForge.Application.Interfaces;

namespace GymForge.Infrastructure.Persistence;

/// <summary>
/// Base de los proveedores de tenant: resuelve el tenant base (sesión, API, tests) y permite
/// pisarlo temporalmente con <see cref="BeginScope"/>. El override vive en un AsyncLocal, así que
/// dos flujos concurrentes (p. ej. dos swipes de puertas distintas) no se pisan entre sí.
/// </summary>
public abstract class ScopedTenantProvider : ICurrentTenant
{
    private readonly AsyncLocal<TenantKey?> _scoped = new();

    protected abstract Guid BaseCompanyId { get; }
    protected abstract Guid BaseSiteId { get; }

    public Guid CompanyId => _scoped.Value?.CompanyId ?? BaseCompanyId;
    public Guid SiteId => _scoped.Value?.SiteId ?? BaseSiteId;

    public IDisposable BeginScope(Guid companyId, Guid siteId)
    {
        var previous = _scoped.Value;
        _scoped.Value = new TenantKey(companyId, siteId);
        return new ScopeRestorer(_scoped, previous);
    }

    private readonly record struct TenantKey(Guid CompanyId, Guid SiteId);

    private sealed class ScopeRestorer(AsyncLocal<TenantKey?> scoped, TenantKey? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            scoped.Value = previous;
        }
    }
}
