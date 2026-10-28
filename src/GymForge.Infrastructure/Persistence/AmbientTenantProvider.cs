namespace GymForge.Infrastructure.Persistence;

/// <summary>
/// Tenant mutable fijado explícitamente con <see cref="Set"/>: lo usan la API (tenant por defecto
/// al arrancar), el design-time de EF y los tests. Sin <see cref="Set"/> el tenant es vacío
/// (Guid.Empty), de modo que los filtros globales no devuelven nada.
/// </summary>
public sealed class AmbientTenantProvider : ScopedTenantProvider
{
    private Guid _companyId;
    private Guid _siteId;

    protected override Guid BaseCompanyId => _companyId;
    protected override Guid BaseSiteId => _siteId;

    /// <summary>Fija el tenant base. Llamar al arrancar, antes de atender consultas concurrentes.</summary>
    public void Set(Guid companyId, Guid siteId)
    {
        _companyId = companyId;
        _siteId = siteId;
    }
}
