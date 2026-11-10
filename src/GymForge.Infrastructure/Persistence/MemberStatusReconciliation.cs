using GymForge.Application.Interfaces;
using GymForge.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure.Persistence;

/// <summary>
/// NEW-31 (ADR-002 addendum): en bases legadas, anteriores a D14, hay socios que quedaron en Prospect aunque ya
/// tienen una membresía Active. El Gatekeeper (allowlist Active/Overdue) los denegaría, así que en el arranque se
/// los pasa a Active. Es una reparación de datos de sistema, no una acción de usuario:
/// - Alcance global (todos los tenants), por UPDATE directo: no pasa por los query filters ni por el interceptor de
///   auditoría (en el boot no hay cajero), por eso no escribe AuditLog. Cada socio se promueve solo por SUS membresías.
/// - UPDATE y no Member.Activate(): evita eventos "activado ahora" falsos y no obliga a inventar una JoinDate.
/// - Solo Prospect: Suspended/Frozen/Expired/Cancelled son sanciones o bajas explícitas y no se pisan.
/// - Idempotente (el WHERE Prospect deja de matchear) y con backup previo solo si hay algo que reparar. Si el backup
///   falla (devuelve false) se loguea un warning y se repara igual: abortar dejaría la recepción sin abrir.
/// Los estados se persisten como texto (nombre del enum), no como int: los literales salen de los enums.
/// </summary>
internal static class MemberStatusReconciliation
{
    internal const string BackupReason = "pre-reconciliacion-estado-socio";

    internal static async Task ReconcileAsync(
        GymForgeDbContext db, IDatabaseBackup backup, ILogger logger, CancellationToken ct = default)
    {
        if (!await MemberDocumentMigrationGuard.MembersTableExistsAsync(db))
            return;

        var prospect = MemberStatus.Prospect.ToString();
        var active = MemberStatus.Active.ToString();
        var activeMembership = MembershipStatus.Active.ToString();

        var anyToFix = await db.Database.SqlQuery<int>(
            $"""
            SELECT EXISTS (
                SELECT 1 FROM Members
                WHERE Status = {prospect}
                  AND EXISTS (SELECT 1 FROM Memberships ms WHERE ms.MemberId = Members.Id AND ms.Status = {activeMembership})
            ) AS Value
            """).SingleAsync(ct) == 1;
        if (!anyToFix)
            return;

        // Best-effort: si no se pudo respaldar se avisa pero se sigue, para no dejar la recepción sin abrir.
        // El cambio es reversible (Active -> Prospect) y la base ya pasó por EnsureHealthy.
        if (!backup.BackupNow(BackupReason))
            logger.LogWarning(
                "Reconciliación de estado de socio: no se pudo crear el backup previo; se continúa sin respaldo");

        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE Members SET Status = {active}, UpdatedAt = {DateTime.UtcNow}
            WHERE Status = {prospect}
              AND EXISTS (SELECT 1 FROM Memberships ms WHERE ms.MemberId = Members.Id AND ms.Status = {activeMembership})
            """, ct);

        logger.LogInformation(
            "Reconciliación de estado de socio: {Count} socio(s) Prospect con membresía activa promovidos a Active",
            affected);
    }
}
