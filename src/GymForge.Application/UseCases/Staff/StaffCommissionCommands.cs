using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Commissions;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Staff;

// Tarifas de comisión por persona. Las comisiones se congelan al vender (Membership/SaleLine): editar un
// porcentaje afecta solo a las ventas futuras.

/// <summary>
/// Porcentajes de comisión (0 a 100) de un miembro del personal. La comisión de personal training no se expone:
/// no hay flujo de venta de PT, así que su porcentaje queda como está.
/// </summary>
public record StaffCommissionRatesDto(
    Guid StaffId, string FullName, StaffRole Role, decimal PctMemberships, decimal PctProducts);

// ── Listar ────────────────────────────────────────────────────────────────────

public record GetStaffCommissionRatesQuery(Guid CompanyId) : IRequest<IReadOnlyList<StaffCommissionRatesDto>>;

public class GetStaffCommissionRatesQueryHandler
    : IRequestHandler<GetStaffCommissionRatesQuery, IReadOnlyList<StaffCommissionRatesDto>>
{
    private readonly IStaffRepository _repo;
    public GetStaffCommissionRatesQueryHandler(IStaffRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<StaffCommissionRatesDto>> Handle(
        GetStaffCommissionRatesQuery q, CancellationToken ct) =>
        (await _repo.GetActiveByCompanyAsync(q.CompanyId, ct))
            .OrderBy(s => s.FirstName).ThenBy(s => s.LastName)
            .Select(s => new StaffCommissionRatesDto(
                s.Id, s.FullName, s.Role, s.CommissionPctMemberships, s.CommissionPctProducts))
            .ToList();
}

// ── Editar ────────────────────────────────────────────────────────────────────

public record UpdateStaffCommissionsCommand(
    Guid CompanyId, Guid StaffId, decimal PctMemberships, decimal PctProducts) : IRequest<StaffCommissionRatesDto>;

public class UpdateStaffCommissionsCommandValidator : AbstractValidator<UpdateStaffCommissionsCommand>
{
    public UpdateStaffCommissionsCommandValidator()
    {
        RuleFor(x => x.PctMemberships).ValidCommissionPct();
        RuleFor(x => x.PctProducts).ValidCommissionPct();
    }
}

public class UpdateStaffCommissionsCommandHandler
    : IRequestHandler<UpdateStaffCommissionsCommand, StaffCommissionRatesDto>
{
    private readonly IStaffRepository _repo;
    public UpdateStaffCommissionsCommandHandler(IStaffRepository repo) => _repo = repo;

    public async Task<StaffCommissionRatesDto> Handle(UpdateStaffCommissionsCommand cmd, CancellationToken ct)
    {
        var staff = await _repo.GetByIdAsync(cmd.StaffId, ct);
        if (staff is null || staff.CompanyId != cmd.CompanyId)
            throw new KeyNotFoundException("El personal no existe.");

        staff.SetCommissions(cmd.PctMemberships, staff.CommissionPctPt, cmd.PctProducts);
        _repo.Update(staff);
        await _repo.SaveChangesAsync(ct);

        return new StaffCommissionRatesDto(
            staff.Id, staff.FullName, staff.Role, staff.CommissionPctMemberships, staff.CommissionPctProducts);
    }
}
