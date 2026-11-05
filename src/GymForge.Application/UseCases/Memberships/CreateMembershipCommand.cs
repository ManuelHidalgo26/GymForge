using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using MediatR;

namespace GymForge.Application.UseCases.Memberships;

public record CreateMembershipCommand(
    Guid CompanyId,
    Guid SiteId,
    Guid MemberId,
    Guid MembershipTypeId,
    DateOnly StartDate,
    bool AutoActivate = true,
    Guid? SoldByStaffId = null) : IRequest<MembershipDto>;

public class CreateMembershipCommandValidator : AbstractValidator<CreateMembershipCommand>
{
    public CreateMembershipCommandValidator(ICompanyCalendar calendar)
    {
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.MembershipTypeId).NotEmpty();
        // Hasta ayer (del gimnasio) para tolerar el alta cargada pasada la medianoche.
        RuleFor(x => x.StartDate).MustAsync(async (cmd, start, ct) =>
            start >= (await calendar.TodayAsync(cmd.CompanyId, ct)).AddDays(-1))
            .WithMessage("La fecha de inicio no puede ser anterior a ayer.");
    }
}

public class CreateMembershipCommandHandler : IRequestHandler<CreateMembershipCommand, MembershipDto>
{
    private readonly IMemberRepository _memberRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly IMembershipTypeRepository _planRepo;

    public CreateMembershipCommandHandler(
        IMemberRepository memberRepo,
        IMembershipRepository membershipRepo,
        IMembershipTypeRepository planRepo)
    {
        _memberRepo = memberRepo;
        _membershipRepo = membershipRepo;
        _planRepo = planRepo;
    }

    public async Task<MembershipDto> Handle(CreateMembershipCommand cmd, CancellationToken ct)
    {
        var member = await _memberRepo.GetByIdAsync(cmd.MemberId, ct)
            ?? throw new KeyNotFoundException($"Socio {cmd.MemberId} no encontrado.");

        var plan = await _planRepo.GetByIdAsync(cmd.MembershipTypeId, ct)
            ?? throw new KeyNotFoundException($"Plan {cmd.MembershipTypeId} no encontrado.");

        // TODO(ADR-002): sembrar VisitsRemaining para planes VisitPack (fuera de alcance de la sesión 1).
        var membership = Membership.Create(
            cmd.CompanyId,
            cmd.SiteId,
            cmd.MemberId,
            cmd.MembershipTypeId,
            cmd.StartDate,
            endDate: plan.CalculateEndDate(cmd.StartDate),
            soldByStaffId: cmd.SoldByStaffId);

        if (cmd.AutoActivate)
        {
            membership.Activate();
            member.Activate(cmd.StartDate);
        }

        await _membershipRepo.AddAsync(membership, ct);
        await _membershipRepo.SaveChangesAsync(ct);

        return MembershipDto.FromEntity(membership);
    }
}

// ── Freeze / Unfreeze ─────────────────────────────────────────────────────────

public record FreezeMembershipCommand(
    Guid MembershipId,
    DateOnly FreezeStart,
    DateOnly FreezeEnd,
    string Reason) : IRequest<MembershipDto>;

public class FreezeMembershipCommandHandler : IRequestHandler<FreezeMembershipCommand, MembershipDto>
{
    private readonly IMembershipRepository _repo;
    private readonly ISiteRepository _sites;
    private readonly ICompanyCalendar _calendar;

    public FreezeMembershipCommandHandler(IMembershipRepository repo, ISiteRepository sites, ICompanyCalendar calendar)
    {
        _repo = repo;
        _sites = sites;
        _calendar = calendar;
    }

    public async Task<MembershipDto> Handle(FreezeMembershipCommand cmd, CancellationToken ct)
    {
        var ms = await _repo.GetByIdAsync(cmd.MembershipId, ct)
            ?? throw new KeyNotFoundException($"Membresía {cmd.MembershipId} no encontrada.");
        var company = await _sites.GetCompanyAsync(ms.CompanyId, ct)
            ?? throw new KeyNotFoundException($"Empresa {ms.CompanyId} no encontrada.");
        EnsureStartCountsAgainstThisYear(cmd.FreezeStart, _calendar.TodayFor(company));

        ms.Freeze(cmd.FreezeStart, cmd.FreezeEnd, cmd.Reason, company.MaxFreezeDaysPerYear);
        _repo.Update(ms);
        await _repo.SaveChangesAsync(ct);
        return MembershipDto.FromEntity(ms);
    }

    /// <summary>
    /// El cupo es anual y se lleva por el año de <c>FreezeStart</c>: una fecha de otro año (o futura) permitiría
    /// pisar el conteo del año en curso alternando fechas.
    /// </summary>
    private static void EnsureStartCountsAgainstThisYear(DateOnly freezeStart, DateOnly today)
    {
        if (freezeStart.Year != today.Year || freezeStart > today)
            throw new InvalidOperationException(
                "El congelamiento tiene que empezar hoy o antes, dentro del año en curso (el cupo es anual).");
    }
}

public record UnfreezeMembershipCommand(Guid MembershipId) : IRequest<MembershipDto>;

public class UnfreezeMembershipCommandHandler : IRequestHandler<UnfreezeMembershipCommand, MembershipDto>
{
    private readonly IMembershipRepository _repo;
    private readonly ICompanyCalendar _calendar;

    public UnfreezeMembershipCommandHandler(IMembershipRepository repo, ICompanyCalendar calendar)
    {
        _repo = repo;
        _calendar = calendar;
    }

    public async Task<MembershipDto> Handle(UnfreezeMembershipCommand cmd, CancellationToken ct)
    {
        var ms = await _repo.GetByIdAsync(cmd.MembershipId, ct)
            ?? throw new KeyNotFoundException($"Membresía {cmd.MembershipId} no encontrada.");

        // Los días sin usar se cuentan contra el "hoy" local: con la fecha UTC, de 21:00 a 24:00 en
        // Argentina el descongelado devolvería un día de menos.
        ms.Unfreeze(await _calendar.TodayAsync(ms.CompanyId, ct));
        _repo.Update(ms);
        await _repo.SaveChangesAsync(ct);
        return MembershipDto.FromEntity(ms);
    }
}

public record CancelMembershipCommand(Guid MembershipId, string Reason) : IRequest<MembershipDto>;

public class CancelMembershipCommandHandler : IRequestHandler<CancelMembershipCommand, MembershipDto>
{
    private readonly IMembershipRepository _repo;
    public CancelMembershipCommandHandler(IMembershipRepository repo) => _repo = repo;

    public async Task<MembershipDto> Handle(CancelMembershipCommand cmd, CancellationToken ct)
    {
        var ms = await _repo.GetByIdAsync(cmd.MembershipId, ct)
            ?? throw new KeyNotFoundException($"Membresía {cmd.MembershipId} no encontrada.");

        ms.Cancel(cmd.Reason);
        _repo.Update(ms);
        await _repo.SaveChangesAsync(ct);
        return MembershipDto.FromEntity(ms);
    }
}
