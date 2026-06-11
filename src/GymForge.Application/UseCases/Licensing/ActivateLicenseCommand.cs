using FluentValidation;
using GymForge.Application.Interfaces;
using MediatR;

namespace GymForge.Application.UseCases.Licensing;

/// <summary>Valida la clave pegada por el usuario, la persiste y aplica el estado en caliente.</summary>
public record ActivateLicenseCommand(Guid CompanyId, string LicenseKey) : IRequest<LicenseState>;

public class ActivateLicenseCommandValidator : AbstractValidator<ActivateLicenseCommand>
{
    public ActivateLicenseCommandValidator() =>
        RuleFor(x => x.LicenseKey).NotEmpty().WithMessage("Pegá la clave de licencia.");
}

public class ActivateLicenseCommandHandler : IRequestHandler<ActivateLicenseCommand, LicenseState>
{
    private readonly ISiteRepository _repo;
    private readonly ILicenseService _licenses;
    private readonly CurrentLicense _current;
    private readonly IClock _clock;

    public ActivateLicenseCommandHandler(
        ISiteRepository repo, ILicenseService licenses, CurrentLicense current, IClock clock)
    {
        _repo = repo;
        _licenses = licenses;
        _current = current;
        _clock = clock;
    }

    public async Task<LicenseState> Handle(ActivateLicenseCommand cmd, CancellationToken ct)
    {
        var state = _licenses.Resolve(cmd.LicenseKey, _clock.Today);

        if (state.Status == LicenseStatus.Expired)
            throw new InvalidOperationException(
                $"La licencia venció el {state.ExpiresOn:dd/MM/yyyy}. Pedí una clave renovada.");
        if (!state.IsPaid)
            throw new InvalidOperationException(
                "La clave no es válida. Revisá que esté copiada completa.");

        var company = await _repo.GetCompanyAsync(cmd.CompanyId, ct)
            ?? throw new InvalidOperationException("La empresa no existe.");

        company.ActivateLicense(cmd.LicenseKey);
        await _repo.SaveChangesAsync(ct);

        _current.State = state;
        return state;
    }
}
