namespace GymForge.Application.Interfaces;

public enum NotificationChannel { WhatsApp, Sms }

/// <summary>Mensaje saliente a un socio (recordatorio de cobro/vencimiento).</summary>
public record NotificationMessage(
    string ToPhone,
    string Body,
    NotificationChannel Channel = NotificationChannel.WhatsApp);

/// <summary>
/// Envía notificaciones a los socios. La implementación real (Twilio / Wassenger) se
/// enchufa por configuración; por defecto se usa un sender que solo registra el mensaje
/// en el log, de modo que el sistema funciona sin proveedor configurado.
/// </summary>
public interface INotificationSender
{
    /// <summary>Envía el mensaje. Devuelve true si se aceptó para envío.</summary>
    Task<bool> SendAsync(NotificationMessage message, CancellationToken ct = default);
}
