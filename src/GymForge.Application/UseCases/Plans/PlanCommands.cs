using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Plans;

// ── Listar todos los planes (administración) ─────────────────────────────────

public record GetAllPlansQuery(Guid CompanyId) : IRequest<IReadOnlyList<MembershipTypeDto>>;

public class GetAllPlansQueryHandler : IRequestHandler<GetAllPlansQuery, IReadOnlyList<MembershipTypeDto>>
{
    private readonly IMembershipTypeRepository _repo;
    public GetAllPlansQueryHandler(IMembershipTypeRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<MembershipTypeDto>> Handle(GetAllPlansQuery q, CancellationToken ct) =>
        (await _repo.GetAllByCompanyAsync(q.CompanyId, ct)).Select(MembershipTypeDto.FromEntity).ToList();
}

// ── Crear plan ────────────────────────────────────────────────────────────────

public record CreatePlanCommand(
    Guid CompanyId,
    string Name,
    MembershipBasis Basis,
    decimal Price,
    int DurationValue,
    string DurationUnit) : IRequest<MembershipTypeDto>;

public class CreatePlanCommandValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("El nombre del plan es obligatorio.");
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo.");
        RuleFor(x => x.DurationValue).GreaterThan(0).WithMessage("La duración debe ser positiva.");
        RuleFor(x => x.DurationUnit).Must(u => u is "Day" or "Month" or "Year")
            .WithMessage("La unidad debe ser Día, Mes o Año.");
    }
}

public class CreatePlanCommandHandler : IRequestHandler<CreatePlanCommand, MembershipTypeDto>
{
    private readonly IMembershipTypeRepository _repo;
    public CreatePlanCommandHandler(IMembershipTypeRepository repo) => _repo = repo;

    public async Task<MembershipTypeDto> Handle(CreatePlanCommand cmd, CancellationToken ct)
    {
        var plan = MembershipType.Create(
            cmd.CompanyId, cmd.Name.Trim(), cmd.Basis, cmd.Price, cmd.DurationValue, cmd.DurationUnit);
        await _repo.AddAsync(plan, ct);
        await _repo.SaveChangesAsync(ct);
        return MembershipTypeDto.FromEntity(plan);
    }
}

// ── Activar / desactivar plan ─────────────────────────────────────────────────

public record SetPlanActiveCommand(Guid PlanId, bool Active) : IRequest<MembershipTypeDto>;

public class SetPlanActiveCommandHandler : IRequestHandler<SetPlanActiveCommand, MembershipTypeDto>
{
    private readonly IMembershipTypeRepository _repo;
    public SetPlanActiveCommandHandler(IMembershipTypeRepository repo) => _repo = repo;

    public async Task<MembershipTypeDto> Handle(SetPlanActiveCommand cmd, CancellationToken ct)
    {
        var plan = await _repo.GetByIdAsync(cmd.PlanId, ct)
            ?? throw new InvalidOperationException("El plan no existe.");

        if (cmd.Active) plan.Activate();
        else plan.Deactivate();

        await _repo.SaveChangesAsync(ct);
        return MembershipTypeDto.FromEntity(plan);
    }
}
