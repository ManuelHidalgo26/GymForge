namespace GymForge.Desktop.Services;

/// <summary>Abre un link de WhatsApp en la app del sistema. Vive en Desktop: lanzar un proceso es cosa del SO.</summary>
public interface IWhatsAppLinkSender
{
    /// <summary>
    /// Abre el link <c>https://wa.me/...</c> con WhatsApp Desktop o, si no está instalado, el navegador
    /// (WhatsApp Web). Devuelve false si el link no es de wa.me o el sistema no pudo abrirlo; nunca lanza.
    /// </summary>
    bool Open(string url);
}
