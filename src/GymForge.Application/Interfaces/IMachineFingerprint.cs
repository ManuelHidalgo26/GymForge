namespace GymForge.Application.Interfaces;

/// <summary>
/// Huella estable de ESTA PC para atar una licencia a una máquina. Formato corto y legible
/// (<c>XXXX-XXXX-XXXX-XXXX</c>) para que el gimnasio la dicte al vendedor.
/// Nunca lanza: si no puede obtenerla devuelve <c>""</c>.
/// </summary>
public interface IMachineFingerprint
{
    string Current();
}
