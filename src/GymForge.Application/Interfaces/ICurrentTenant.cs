namespace GymForge.Application.Interfaces;

/// <summary>Tenant activo (empresa + sede) para los filtros globales de EF Core.</summary>
public interface ICurrentTenant
{
    Guid CompanyId { get; }
    Guid SiteId { get; }

    /// <summary>
    /// Override temporal del tenant (Gatekeeper = sede de la puerta; API = tenant del request).
    /// Al disponer, restaura el anterior. Se aísla por flujo async (AsyncLocal).
    /// </summary>
    IDisposable BeginScope(Guid companyId, Guid siteId);
}
