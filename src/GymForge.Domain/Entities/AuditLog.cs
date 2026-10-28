namespace GymForge.Domain.Entities;

/// <summary>
/// Append-only audit log. Never update or delete rows. Las filas las escribe el
/// AuditSaveChangesInterceptor (UserId null = operación de sistema).
/// </summary>
public class AuditLog : ITenantScoped
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? UserId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? DiffJson { get; private set; }
    public string? Ip { get; private set; }
    public DateTime Timestamp { get; private set; } = DateTime.UtcNow;
    public Guid CompanyId { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        Guid companyId,
        Guid? userId,
        string entityType,
        Guid entityId,
        string action,
        string? diffJson = null,
        string? ip = null,
        DateTime? timestamp = null) =>
        new()
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            DiffJson = diffJson,
            Ip = ip,
            Timestamp = timestamp ?? DateTime.UtcNow
        };
}
