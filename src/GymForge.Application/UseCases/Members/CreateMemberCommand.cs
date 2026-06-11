using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Members;

public record CreateMemberCommand(
    Guid CompanyId,
    Guid SiteId,
    string FirstName,
    string LastName,
    DocumentType DocumentType,
    string DocumentNumber,
    Gender Gender,
    string? Email,
    string? Mobile,
    DateOnly? BirthDate,
    MemberSource Source = MemberSource.WalkIn,
    Guid? SalesRepId = null,
    bool MarketingConsent = false,
    bool ActivateImmediately = false) : IRequest<MemberDto>;

public class CreateMemberCommandValidator : AbstractValidator<CreateMemberCommand>
{
    public CreateMemberCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Mobile).MaximumLength(30);
        RuleFor(x => x.BirthDate)
            .Must(d => !d.HasValue || d.Value <= DateOnly.FromDateTime(DateTime.Today.AddYears(-5)))
            .WithMessage("La fecha de nacimiento parece incorrecta.");
    }
}

public class CreateMemberCommandHandler : IRequestHandler<CreateMemberCommand, MemberDto>
{
    private readonly IMemberRepository _repo;
    private readonly Licensing.CurrentLicense _license;

    public CreateMemberCommandHandler(IMemberRepository repo, Licensing.CurrentLicense license)
    {
        _repo = repo;
        _license = license;
    }

    public async Task<MemberDto> Handle(CreateMemberCommand cmd, CancellationToken ct)
    {
        var count = await _repo.CountByCompanyAsync(cmd.CompanyId, ct);
        if (count >= _license.State.MaxMembers)
            throw new InvalidOperationException(
                $"Alcanzaste el límite de {_license.State.MaxMembers} socios del plan " +
                $"{_license.State.Tier}. Activá una licencia en Configuración → Licencia.");

        var member = Member.Create(
            cmd.CompanyId,
            cmd.SiteId,
            cmd.FirstName,
            cmd.LastName,
            cmd.DocumentType,
            cmd.DocumentNumber,
            cmd.Gender);

        member.UpdateContact(cmd.Email, cmd.Mobile);
        member.CompleteProfile(cmd.BirthDate, cmd.Source, cmd.SalesRepId, cmd.MarketingConsent);

        // Alta directa como socio activo (mostrador); sin esto queda Prospecto
        // hasta que compre una membresía.
        if (cmd.ActivateImmediately)
            member.Activate(DateOnly.FromDateTime(DateTime.Today));

        await _repo.AddAsync(member, ct);
        await _repo.SaveChangesAsync(ct);

        return MemberDto.FromEntity(member);
    }
}
