using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog;

namespace GymForge.Desktop.Services;

public sealed partial class WhatsAppLinkSender : IWhatsAppLinkSender
{
    /// <remarks>
    /// Solo abre links con el formato exacto que arma <c>WhatsAppLink.Build</c>: con <c>UseShellExecute</c> el
    /// sistema ejecuta lo que le pasen (un .exe, una ruta), así que no se le entrega nada que no sea ese link.
    /// </remarks>
    public bool Open(string url)
    {
        if (!IsWhatsAppLink(url))
        {
            Log.Warning("Se rechazó abrir un link que no es de WhatsApp");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo abrir el link de WhatsApp");
            return false;
        }
    }

    /// <summary>
    /// Allowlist estricta: <c>https://wa.me/&lt;dígitos&gt;[?text=&lt;url-encoded&gt;]</c>, sin nada más.
    /// A propósito NO se valida con <c>Uri.TryCreate</c>: tolera comillas, espacios, saltos de línea, user@host y
    /// espacios al borde, y a <c>ShellExecute</c> le llega el string original, no el normalizado (un <c>"</c>
    /// permite inyectar argumentos en los navegadores cuyo comando registrado lleva el link entre comillas).
    /// </summary>
    public static bool IsWhatsAppLink(string? url) => url is not null && WhatsAppUrlPattern().IsMatch(url);

    [GeneratedRegex(@"\Ahttps://wa\.me/[0-9]{1,20}(\?text=([A-Za-z0-9\-._~]|%[0-9A-Fa-f]{2})*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex WhatsAppUrlPattern();
}
