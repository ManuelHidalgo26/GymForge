namespace GymForge.Domain.Entities;

/// <summary>
/// Marker: el AuditSaveChangesInterceptor registra los inserts, updates y deletes de esta entidad en el
/// AuditLog. Lista explícita (como ITenantScoped): una entidad nueva de negocio hay que sumarla acá.
/// Solo para entidades que heredan de BaseEntity: el interceptor identifica la fila auditada por su Id.
/// Nunca la implementan AuditLog, AccessLog ni MemberCreditMovement (ya son append-only por diseño).
/// </summary>
public interface IAuditable { }
