namespace GymForge.Application.UseCases.Licensing;

/// <summary>Contenido firmado de una clave de licencia.</summary>
public record LicensePayload(
    Guid LicenseId,
    string Gym,
    string Cuit,
    string Tier,
    int MaxSites,
    int MaxMembers,
    DateOnly IssuedOn,
    DateOnly ExpiresOn);

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
public sealed record LicenseState(
    LicenseStatus Status,
    string Tier,
    int MaxSites,
    int MaxMembers,
    DateOnly? ExpiresOn,
    string? GymName)
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
