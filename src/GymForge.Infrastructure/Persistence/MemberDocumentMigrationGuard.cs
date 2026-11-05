using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure.Persistence;

/// <summary>
/// Se lanza cuando la base viva tiene socios con el mismo documento en la misma (empresa, sede) y por eso
/// no se puede crear el índice único de DNI. Nunca se corrige solo: borrar o fusionar socios es decisión humana.
/// </summary>
public sealed class DuplicateMemberDocumentsBlockMigrationException(string message) : InvalidOperationException(message);

/// <summary>
/// Chequeo de salud previo a migrar: la migración D7 crea un índice único sobre (CompanyId, SiteId, DocumentNumber)
/// y fallaría con un error oscuro de SQLite si la base ya tiene DNIs repetidos. Es mantenimiento, no una
/// consulta de negocio, así que mira todas las empresas/sedes (sin filtro de tenant).
/// </summary>
internal static class MemberDocumentMigrationGuard
{
    internal const string UniqueIndexMigrationId = "20261105001255_AddMemberDocumentUniqueIndex";
    private const int MaxGroupsInMessage = 20;
    private const int VisibleDocumentDigits = 3;
    private const char MaskChar = '•';

    internal sealed class DuplicateGroup
    {
        public string SiteName { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
        public int Total { get; set; }
    }

    internal static async Task EnsureNoDuplicatesAsync(GymForgeDbContext db, ILogger logger)
    {
        var pending = await db.Database.GetPendingMigrationsAsync();
        if (!pending.Contains(UniqueIndexMigrationId) || !await MembersTableExistsAsync(db))
            return;

        var groups = await FindDuplicateGroupsAsync(db);
        if (groups.Count == 0)
            return;

        var message = BuildMessage(groups);
        logger.LogCritical(
            "Migración bloqueada por DNIs duplicados: {Groups} grupo(s), {Members} socio(s) afectados",
            groups.Count, groups.Sum(g => g.Total));
        throw new DuplicateMemberDocumentsBlockMigrationException(message);
    }

    /// <summary>Deja visibles solo los últimos 3 dígitos: el operador ubica al socio sin que el DNI completo llegue al log.</summary>
    private static string Mask(string document) =>
        document.Length <= VisibleDocumentDigits
            ? new string(MaskChar, VisibleDocumentDigits)
            : new string(MaskChar, document.Length - VisibleDocumentDigits) + document[^VisibleDocumentDigits..];

    private static async Task<bool> MembersTableExistsAsync(GymForgeDbContext db) =>
        (await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = 'Members'")
            .SingleAsync()) > 0;

    private static Task<List<DuplicateGroup>> FindDuplicateGroupsAsync(GymForgeDbContext db) =>
        db.Database.SqlQueryRaw<DuplicateGroup>(
            """
            SELECT COALESCE(s.Name, '(sede desconocida)') AS SiteName,
                   m.DocumentNumber AS DocumentNumber,
                   COUNT(*) AS Total
            FROM Members m
            LEFT JOIN Sites s ON s.Id = m.SiteId
            WHERE m.DocumentNumber <> ''
            GROUP BY m.CompanyId, m.SiteId, m.DocumentNumber
            HAVING COUNT(*) > 1
            ORDER BY SiteName, m.DocumentNumber
            """).ToListAsync();

    private static string BuildMessage(IReadOnlyList<DuplicateGroup> groups)
    {
        var text = new StringBuilder();
        text.AppendLine(
            $"No se puede actualizar la base: hay {groups.Count} documento(s) repetido(s) entre socios de la misma sede " +
            $"({groups.Sum(g => g.Total)} socios involucrados). La nueva versión exige un DNI único por sede.");
        foreach (var group in groups.Take(MaxGroupsInMessage))
            text.AppendLine($"  - Sede \"{group.SiteName}\": DNI {Mask(group.DocumentNumber)} x{group.Total}");
        if (groups.Count > MaxGroupsInMessage)
            text.AppendLine($"  ... y {groups.Count - MaxGroupsInMessage} más.");
        text.Append(
            "Qué hacer: 1) cerrar la app y hacer una copia de seguridad de la base; 2) corregir o unificar esos socios " +
            "con la versión anterior (editar el DNI equivocado o dar de baja el duplicado); 3) volver a abrir esta versión. " +
            "No se modificó ni se borró ningún dato.");
        return text.ToString();
    }
}
