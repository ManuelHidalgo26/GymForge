using System.Globalization;
using System.IO;
using GymForge.Application.UseCases.Dunning;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GymForge.Desktop.Services;

/// <summary>
/// Dispara el cobro automático (dunning) una vez por día al abrir la app. Best-effort:
/// nunca lanza. Usa un archivo marcador para no repetir el mismo día. Queda dormido si
/// <see cref="DunningConfig.Enabled"/> es false (sin proveedor configurado).
/// </summary>
public static class DunningStartup
{
    private static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GymForge", "dunning-last-run.txt");

    public static async Task RunDailyAsync(IServiceProvider services)
    {
        try
        {
            var session = services.GetRequiredService<SessionContext>();
            if (session.CompanyId == Guid.Empty) return;

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (ReadLastRun() == today) return;   // ya corrió hoy

            using var scope = services.CreateScope();
            var dunning = scope.ServiceProvider.GetRequiredService<DunningService>();
            var sent = await dunning.RunAsync(session.CompanyId, today, session.GymName);
            if (sent > 0) Log.Information("Dunning: {Count} recordatorios enviados", sent);

            WriteLastRun(today);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Dunning: no se pudo correr el cobro automático");
        }
    }

    private static DateOnly? ReadLastRun()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return null;
            return DateOnly.TryParse(File.ReadAllText(MarkerPath).Trim(),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        }
        catch { return null; }
    }

    private static void WriteLastRun(DateOnly date)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        catch { /* best-effort */ }
    }
}
