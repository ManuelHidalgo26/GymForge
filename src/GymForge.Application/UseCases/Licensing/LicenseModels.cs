using System.Text.Json.Serialization;

namespace GymForge.Application.UseCases.Licensing;

/// <summary>
/// Contenido firmado de una clave de licencia. <paramref name="MachineId"/> es opcional: sin él la clave
/// es portable (activa en cualquier PC); con él queda atada al código de una PC (ver <see cref="MachineCode"/>).
/// Es el último campo y no se escribe cuando es nulo, así las claves emitidas antes de que existiera
/// (JSON de 8 campos) siguen verificando y una clave portable conserva el JSON de siempre.
/// </summary>
public record LicensePayload(
    Guid LicenseId,
    string Gym,
    string Cuit,
    string Tier,
    int MaxSites,
    int MaxMembers,
    DateOnly IssuedOn,
    DateOnly ExpiresOn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MachineId = null);

public enum LicenseStatus
{
    /// <summary>Sin licencia: límites del tier gratuito.</summary>
    Free,
    Active,
    /// <summary>Vencida hace poco: sigue operativa mientras renueva (offline-first).</summary>
    Grace,
    /// <summary>Vencida fuera de gracia: vuelve a los límites Free.</summary>
    Expired,
}

/// <summary>Estado de licencia resuelto, con los límites efectivos a aplicar.</summary>
/// <param name="Cuit">CUIT firmado en la clave (vacío o nulo si la clave no lo trae).</param>
/// <param name="MachineMismatch">
/// La clave está atada a otra PC. Es solo un aviso: no cambia <paramref name="Status"/> ni los límites.
/// </param>
public sealed record LicenseState(
    LicenseStatus Status,
    string Tier,
    int MaxSites,
    int MaxMembers,
    DateOnly? ExpiresOn,
    string? GymName,
    string? Cuit = null,
    bool MachineMismatch = false)
{
    public const int FreeMaxSites = 1;
    public const int FreeMaxMembers = 50;

    public static LicenseState Free { get; } =
        new(LicenseStatus.Free, "Free", FreeMaxSites, FreeMaxMembers, null, null);

    public bool IsPaid => Status is LicenseStatus.Active or LicenseStatus.Grace;
}

/// <summary>
/// Estado de licencia vivo de la app (singleton). Se resuelve al arrancar desde
/// Company.LicenseKey y se actualiza en caliente al activar una clave.
/// Mismo patrón que GatekeeperConfig.
/// </summary>
public class CurrentLicense
{
    public LicenseState State { get; set; } = LicenseState.Free;
}
