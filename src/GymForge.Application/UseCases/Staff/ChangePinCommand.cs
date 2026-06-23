using FluentValidation;
using GymForge.Application.Interfaces;
using MediatR;

namespace GymForge.Application.UseCases.Staff;

/// <summary>
/// Cambia el PIN de un cajero/admin. El login es por PIN (sin usuario), así que el
/// PIN actual identifica al staff cuyo código se reemplaza.
/// </summary>
public record ChangePinCommand(Guid CompanyId, string CurrentPin, string NewPin) : IRequest;

public class ChangePinCommandValidator : AbstractValidator<ChangePinCommand>
{
    public ChangePinCommandValidator()
    {
        RuleFor(x => x.CurrentPin).NotEmpty().WithMessage("Ingresá el PIN actual.");
        RuleFor(x => x.NewPin)
            .NotEmpty().WithMessage("Ingresá el nuevo PIN.")
            .Matches(@"^\d{4,8}$").WithMessage("El PIN debe tener entre 4 y 8 dígitos.");
        RuleFor(x => x.NewPin)
            .NotEqual(x => x.CurrentPin).WithMessage("El nuevo PIN debe ser distinto al actual.");
    }
}

public class ChangePinCommandHandler : IRequestHandler<ChangePinCommand>
{
    private readonly IStaffRepository _repo;
    private readonly IPinHasher _hasher;

    public ChangePinCommandHandler(IStaffRepository repo, IPinHasher hasher)
    {
        _repo = repo;
        _hasher = hasher;
    }

    public async Task Handle(ChangePinCommand cmd, CancellationToken ct)
    {
        var staff = await _repo.GetActiveByCompanyAsync(cmd.CompanyId, ct);

        var match = staff.FirstOrDefault(s => _hasher.Verify(cmd.CurrentPin, s.PinCodeHash))
            ?? throw new InvalidOperationException("El PIN actual es incorrecto.");

        // El login resuelve el staff por su PIN: dos personas no pueden compartirlo.
        if (staff.Any(s => s.Id != match.Id && _hasher.Verify(cmd.NewPin, s.PinCodeHash)))
            throw new InvalidOperationException("Ese PIN ya lo usa otro miembro del staff. Elegí otro.");

        match.ChangePin(_hasher.Hash(cmd.NewPin));
        _repo.Update(match);
        await _repo.SaveChangesAsync(ct);
    }
}

/// <summary>
/// True si algún cajero activo todavía usa el PIN de fábrica (1234). Se usa para
/// avisar en Configuración que hay que cambiarlo antes de poner el gimnasio en marcha.
/// </summary>
public record CheckDefaultPinQuery(Guid CompanyId) : IRequest<bool>;

public class CheckDefaultPinQueryHandler : IRequestHandler<CheckDefaultPinQuery, bool>
{
    public const string DefaultPin = "1234";

    private readonly IStaffRepository _repo;
    private readonly IPinHasher _hasher;

    public CheckDefaultPinQueryHandler(IStaffRepository repo, IPinHasher hasher)
    {
        _repo = repo;
        _hasher = hasher;
    }

    public async Task<bool> Handle(CheckDefaultPinQuery query, CancellationToken ct)
    {
        var staff = await _repo.GetActiveByCompanyAsync(query.CompanyId, ct);
        return staff.Any(s => _hasher.Verify(DefaultPin, s.PinCodeHash));
    }
}
