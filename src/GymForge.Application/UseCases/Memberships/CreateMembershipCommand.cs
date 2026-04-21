using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
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
    public CreateMembershipCommandValidator()
    {
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.MembershipTypeId).NotEmpty();
        RuleFor(x => x.StartDate).GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
    }
}

public class CreateMembershipCommandHandler : IRequestHandler<CreateMembershipCommand, MembershipDto>
{
    private readonly IMemberRepository _memberRepo;
    private readonly IMembershipRepository _membershipRepo;

    public CreateMembershipCommandHandler(
        IMemberRepository memberRepo,
        IMembershipRepository membershipRepo)
    {
        _memberRepo = memberRepo;
        _membershipRepo = membershipRepo;
    }

    public async Task<MembershipDto> Handle(CreateMembershipCommand cmd, CancellationToken ct)
    {
        var member = await _memberRepo.GetByIdAsync(cmd.MemberId, ct)
            ?? throw new KeyNotFoundException($"Socio {cmd.MemberId} no encontrado.");

        // Get membership type to calculate end date
        // For now create without end date — infrastructure will load the type
        var membership = Membership.Create(
            cmd.CompanyId,
            cmd.SiteId,
            cmd.MemberId,
            cmd.MembershipTypeId,
            cmd.StartDate,
            endDate: null,
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
    public FreezeMembershipCommandHandler(IMembershipRepository repo) => _repo = repo;

    public async Task<MembershipDto> Handle(FreezeMembershipCommand cmd, CancellationToken ct)
    {
        var ms = await _repo.GetByIdAsync(cmd.MembershipId, ct)
            ?? throw new KeyNotFoundException($"Membresía {cmd.MembershipId} no encontrada.");

        ms.Freeze(cmd.FreezeStart, cmd.FreezeEnd, cmd.Reason);
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
