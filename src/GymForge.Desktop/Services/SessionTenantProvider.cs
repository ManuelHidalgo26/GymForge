using GymForge.Infrastructure.Persistence;

namespace GymForge.Desktop.Services;

/// <summary>
/// Tenant de los filtros globales de EF en el escritorio: la empresa y la sede activas de la
/// <see cref="SessionContext"/>, leídas en vivo (cambiar de sede en el topbar cambia el filtro).
/// El Gatekeeper lo pisa con <c>BeginScope</c> para resolver la sede de la puerta.
/// </summary>
public sealed class SessionTenantProvider(SessionContext session) : ScopedTenantProvider
{
    protected override Guid BaseCompanyId => session.CompanyId;
    protected override Guid BaseSiteId => session.SiteId;
}
