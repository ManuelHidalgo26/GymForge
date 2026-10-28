namespace GymForge.Domain.Entities;

/// <summary>Entidad que pertenece a una empresa: el DbContext la filtra por <c>CompanyId</c>.</summary>
public interface ITenantScoped
{
    Guid CompanyId { get; }
}

/// <summary>Entidad que pertenece a una sede de una empresa: se filtra por <c>CompanyId</c> + <c>SiteId</c>.</summary>
public interface ISiteScoped : ITenantScoped
{
    Guid SiteId { get; }
}
