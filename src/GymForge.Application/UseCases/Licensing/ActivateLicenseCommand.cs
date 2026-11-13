using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using GymForge.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GymForge.Application.UseCases.Licensing;

/// <summary>Valida la clave pegada por el usuario, la persiste y aplica el estado en caliente.</summary>
public record ActivateLicenseCommand(Guid CompanyId, string LicenseKey) : IRequest<LicenseState>;

public class ActivateLicenseCommandValidator : AbstractValidator<ActivateLicenseCommand>
{
    public ActivateLicenseCommandValidator() =>
        RuleFor(x => x.LicenseKey).NotEmpty().WithMessage("Pegá la clave de licencia.");
}

public sealed partial class ActivateLicenseCommandHandler : IRequestHandler<ActivateLicenseCommand, LicenseState>
{
    private readonly ISiteRepository _repo;
    private readonly ILicenseService _licenses;
    private readonly CurrentLicense _current;
    private readonly ICompanyCalendar _calendar;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly ILogger<ActivateLicenseCommandHandler> _log;

    public ActivateLicenseCommandHandler(
        ISiteRepository repo, ILicenseService licenses, CurrentLicense current, ICompanyCalendar calendar,
        ICurrentUser user, IClock clock, ILogger<ActivateLicenseCommandHandler> log)
    {
        _user = user;
        _clock = clock;
        _repo = repo;
        _licenses = licenses;
        _current = current;
        _calendar = calendar;
        _log = log;
    }

    public async Task<LicenseState> Handle(ActivateLicenseCommand cmd, CancellationToken ct)
    {
        var state = _licenses.Resolve(cmd.LicenseKey, await _calendar.TodayAsync(cmd.CompanyId, ct));

        if (state.Status == LicenseStatus.Expired)
            throw new InvalidOperationException(
                $"La licencia venció el {state.ExpiresOn:dd/MM/yyyy}. Pedí una clave renovada.");
        if (!state.IsPaid)
            throw new InvalidOperationException(
                "La clave no es válida. Revisá que esté copiada completa.");

        var company = await _repo.GetCompanyAsync(cmd.CompanyId, ct)
            ?? throw new InvalidOperationException("La empresa no existe.");

        await EnsureKeyBelongsToAsync(company, state, ct);

        var wasLicensed = company.LicenseKey is not null;
        company.ActivateLicense(cmd.LicenseKey);
        await _repo.AppendAuditLogAsync(NewAudit(company.Id, "LicenseActivated", new
        {
            tier = state.Tier,
            expiresOn = state.ExpiresOn,
            status = state.Status.ToString(),
            renewal = wasLicensed,
            machineMismatch = state.MachineMismatch
        }), ct);
        await _repo.SaveChangesAsync(ct);

        _current.State = state;
        return state;
    }

    /// <summary>Resumen sin PII (ADR-013): nunca la clave cruda ni ningún CUIT.</summary>
    private AuditLog NewAudit(Guid companyId, string action, object summary) =>
        AuditLog.Create(
            companyId, _user.StaffId, nameof(Company), companyId, action,
            JsonSerializer.Serialize(summary), timestamp: _clock.Now);

    /// <summary>
    /// Deja constancia del rechazo. Best-effort: si la escritura falla se loguea y el rechazo al usuario
    /// se mantiene igual (no se enmascara con el error de auditoría).
    /// </summary>
    private async Task TryAuditRejectionAsync(Company company, LicenseState state, CancellationToken ct)
    {
        try
        {
            await _repo.AppendAuditLogAsync(NewAudit(company.Id, "LicenseActivationRejected", new
            {
                reason = "CuitMismatch",
                keyTier = state.Tier,
                keyExpiresOn = state.ExpiresOn
            }), ct);
            await _repo.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            LogRejectionAuditFailed(ex);
        }
    }

    /// <summary>
    /// Bloquea una clave emitida para otro CUIT. Se chequea solo al activar (no en cada arranque), así que
    /// nunca deja colgado a un gimnasio en operación: apenas pide pegar la clave correcta. Una clave sin CUIT
    /// (emitida antes de que se firmara) o un CUIT que no se puede interpretar no bloquean: un typo del
    /// vendedor no debe dejar afuera a un cliente.
    /// </summary>
    private async Task EnsureKeyBelongsToAsync(Company company, LicenseState state, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state.Cuit)) return;

        if (!Cuit.TryParse(state.Cuit, out var keyCuit) || !Cuit.TryParse(company.TaxId, out var companyCuit))
        {
            LogCuitNotComparable(state.Cuit, company.TaxId);
            return;
        }

        if (keyCuit == companyCuit) return;

        await TryAuditRejectionAsync(company, state, ct);
        throw new InvalidOperationException(
            $"La clave es para otro CUIT ({keyCuit}) y este gimnasio tiene el {companyCuit}. " +
            "Verificá que sea la licencia de este gimnasio.");
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Licencia: no se pudo registrar en la auditoría el rechazo de la clave; se rechaza igual")]
    private partial void LogRejectionAuditFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Licencia: no se pudo comparar el CUIT de la clave ({KeyCuit}) con el de la empresa ({CompanyCuit}); se activa igual")]
    private partial void LogCuitNotComparable(string keyCuit, string companyCuit);
}
