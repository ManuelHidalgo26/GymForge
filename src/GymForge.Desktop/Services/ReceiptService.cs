using System.Diagnostics;
using System.IO;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Charges;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GymForge.Desktop.Services;

/// <summary>
/// Genera el recibo PDF de un pago, lo guarda en
/// %LOCALAPPDATA%\GymForge\recibos\AAAA-MM\ y lo abre con el visor predeterminado.
/// </summary>
public class ReceiptService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ReceiptService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>Best-effort: nunca lanza — un recibo fallido no debe romper el cobro.</summary>
    public async Task<string?> TryGenerateAndOpenAsync(
        Guid paymentId, Guid companyId, CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var writer = scope.ServiceProvider.GetRequiredService<IReceiptPdfWriter>();

            var receipt = await mediator.Send(new BuildReceiptQuery(paymentId, companyId), ct);
            var bytes = writer.Generate(receipt);

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GymForge", "recibos", receipt.IssuedAt.ToString("yyyy-MM"));
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"{receipt.Code}.pdf");
            await File.WriteAllBytesAsync(path, bytes, ct);

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return path;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo generar el recibo del pago {PaymentId}", paymentId);
            return null;
        }
    }
}
