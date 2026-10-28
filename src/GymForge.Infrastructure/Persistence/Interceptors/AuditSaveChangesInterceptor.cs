using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GymForge.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Registra en AuditLog los inserts, updates y deletes de las entidades <see cref="IAuditable"/>, en la
/// misma transacción del SaveChanges. Solo INSERTA filas de AuditLog (append-only) y nunca audita las
/// suyas: AuditLog no es IAuditable, así que agregarlas al contexto no dispara otra auditoría.
/// </summary>
public sealed class AuditSaveChangesInterceptor(ICurrentUser user, IClock clock, ICurrentTenant tenant)
    : SaveChangesInterceptor
{
    private const string RedactedValue = "[redacted]";

    // Timestamps de BaseEntity: el DbContext los actualiza solo, sin que cambie nada de negocio.
    private static readonly HashSet<string> TimestampProperties =
        [nameof(BaseEntity.CreatedAt), nameof(BaseEntity.UpdatedAt)];

    // Credenciales y datos de salud: se registra que cambiaron, nunca su valor. El AuditLog es append-only
    // (no se puede purgar), así que un dato sensible que entra acá queda para siempre, incluso si el socio
    // se elimina o pide la supresión de sus datos.
    private static readonly HashSet<string> RedactedProperties =
    [
        nameof(Staff.PinCodeHash),
        nameof(Member.MedicalConditions),
        nameof(Member.Medications),
        nameof(Member.Allergies)
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping   // legible con acentos; no se embebe en HTML
    };

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        AppendAuditRows(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AppendAuditRows(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        DiscardQueuedAuditRows(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        DiscardQueuedAuditRows(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        DiscardQueuedAuditRows(eventData.Context);
        base.SaveChangesCanceled(eventData);
    }

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        DiscardQueuedAuditRows(eventData.Context);
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    private void AppendAuditRows(DbContext? context)
    {
        if (context is null) return;

        // Se materializa antes de agregar: el ChangeTracker no se puede mutar mientras se enumera.
        var rows = context.ChangeTracker.Entries()
            .Where(IsAudited)
            .Select(BuildAuditRow)
            .OfType<AuditLog>()
            .ToList();

        if (rows.Count > 0)
            context.AddRange(rows);
    }

    /// <summary>
    /// Si el guardado falla o se cancela, las filas de auditoría encoladas se descartan. Las entidades siguen
    /// pendientes en el tracker (el DbContext del escritorio vive toda la app) y el próximo guardado las vuelve
    /// a auditar; sin esto el reintento duplicaría filas o dejaría rastro de operaciones que nunca ocurrieron.
    /// </summary>
    private static void DiscardQueuedAuditRows(DbContext? context)
    {
        if (context is null) return;

        var queued = context.ChangeTracker.Entries<AuditLog>()
            .Where(entry => entry.State == EntityState.Added)
            .ToList();

        foreach (var entry in queued)
            entry.State = EntityState.Detached;
    }

    private static bool IsAudited(EntityEntry entry) =>
        entry.Entity is IAuditable &&
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    /// <summary>Null = no hay nada que registrar (un update donde solo cambió el timestamp).</summary>
    private AuditLog? BuildAuditRow(EntityEntry entry)
    {
        var diffJson = entry.State switch
        {
            EntityState.Added => null,
            EntityState.Modified => ChangedValuesJson(entry),
            _ => SnapshotJson(entry)
        };
        if (entry.State == EntityState.Modified && diffJson is null)
            return null;

        return AuditLog.Create(
            CompanyIdOf(entry.Entity),
            user.StaffId,
            entry.Entity.GetType().Name,
            ((BaseEntity)entry.Entity).Id,
            ActionOf(entry.State),
            diffJson,
            timestamp: clock.Now);
    }

    private static string ActionOf(EntityState state) => state switch
    {
        EntityState.Added => "Insert",
        EntityState.Modified => "Update",
        _ => "Delete"
    };

    /// <summary>La empresa sale de la propia entidad; si no tiene (p. ej. CashMovement), del tenant activo.</summary>
    private Guid CompanyIdOf(object entity) =>
        entity is ITenantScoped { CompanyId: var companyId } && companyId != Guid.Empty
            ? companyId
            : tenant.CompanyId;

    /// <summary>
    /// Solo las propiedades escalares cuyo valor realmente cambió. Se compara original vs. actual porque
    /// DbSet.Update marca TODAS como modificadas aunque no haya cambiado ninguna.
    /// </summary>
    private static string? ChangedValuesJson(EntityEntry entry)
    {
        var changes = entry.Properties
            .Where(p => p.IsModified
                        && !TimestampProperties.Contains(p.Metadata.Name)
                        && !SameValue(p.OriginalValue, p.CurrentValue))
            .ToDictionary(
                p => p.Metadata.Name,
                p => new ValueChange(
                    Present(p.Metadata.Name, p.OriginalValue),
                    Present(p.Metadata.Name, p.CurrentValue)));

        return changes.Count == 0 ? null : JsonSerializer.Serialize(changes, JsonOptions);
    }

    /// <summary>Los deletes son raros y peligrosos: se conserva el dato borrado.</summary>
    private static string SnapshotJson(EntityEntry entry) =>
        JsonSerializer.Serialize(
            entry.Properties.ToDictionary(p => p.Metadata.Name, p => Present(p.Metadata.Name, p.OriginalValue)),
            JsonOptions);

    private static bool SameValue(object? original, object? current) =>
        original is byte[] originalBytes && current is byte[] currentBytes
            ? originalBytes.AsSpan().SequenceEqual(currentBytes)
            : Equals(original, current);

    /// <summary>
    /// Credenciales y datos de salud ocultos (un campo vacío sigue siendo null: no se inventa un valor) y
    /// binarios (huella) reducidos a su tamaño: nunca base64 en la base.
    /// </summary>
    private static object? Present(string propertyName, object? value) => value switch
    {
        not null when RedactedProperties.Contains(propertyName) => RedactedValue,
        byte[] bytes => $"<{bytes.Length} bytes>",
        _ => value
    };

    private sealed record ValueChange(
        [property: JsonPropertyName("old")] object? Old,
        [property: JsonPropertyName("new")] object? New);
}
