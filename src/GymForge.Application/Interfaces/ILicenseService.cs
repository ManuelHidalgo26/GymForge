using GymForge.Application.UseCases.Licensing;

namespace GymForge.Application.Interfaces;

/// <summary>Valida claves de licencia firmadas y resuelve el estado/límites efectivos.</summary>
public interface ILicenseService
{
    /// <summary>Free si la clave es nula, mal formada o con firma inválida.</summary>
    LicenseState Resolve(string? licenseKey, DateOnly today);
}
