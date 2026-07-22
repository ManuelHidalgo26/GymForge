using GymForge.Application.Interfaces;
using Serilog;

namespace GymForge.Infrastructure.Services;

/// <summary>
/// Sender por defecto: registra el mensaje en el log en vez de enviarlo. Permite operar
/// (y probar el dunning) sin proveedor. El envío real (Twilio / Wassenger) se implementa
/// como otro <see cref="INotificationSender"/> y se registra en su lugar.
/// </summary>
public class LogNotificationSender : INotificationSender
{
    public Task<bool> SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        Log.Information("[Notificación {Channel} → {Phone}] {Body}",
            message.Channel, message.ToPhone, message.Body);
        return Task.FromResult(true);
    }
}
