using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using MediatR;

namespace GymForge.Application.UseCases.Members;

/// <summary>
/// Asigna (o reasigna) la credencial de acceso del socio: el código de su carnet o llavero, que después lee el
/// scanner. Es única por empresa: si otro socio ya la tiene, se rechaza.
/// </summary>
public record AssignCredentialCommand(Guid CompanyId, Guid MemberId, string TagSerial) : IRequest<MemberDto>;

public class AssignCredentialCommandValidator : AbstractValidator<AssignCredentialCommand>
{
    public AssignCredentialCommandValidator()
    {
        RuleFor(x => x.TagSerial)
            .NotEmpty().WithMessage("Ingresá el código de la credencial.")
            .MaximumLength(CredentialEnrollment.MaxLength);
    }
}

public class AssignCredentialCommandHandler : IRequestHandler<AssignCredentialCommand, MemberDto>
{
    private readonly IMemberRepository _repo;
    public AssignCredentialCommandHandler(IMemberRepository repo) => _repo = repo;

    public async Task<MemberDto> Handle(AssignCredentialCommand cmd, CancellationToken ct)
    {
        var member = await _repo.GetByIdAsync(cmd.MemberId, ct);
        if (member is null || member.CompanyId != cmd.CompanyId)
            throw new KeyNotFoundException("El socio no existe.");

        var tagSerial = cmd.TagSerial.Trim();
        await CredentialEnrollment.EnsureAvailableAsync(_repo, cmd.CompanyId, tagSerial, member.Id, ct);

        member.EnrollTag(tagSerial);
        _repo.Update(member);
        await _repo.SaveChangesAsync(ct);

        return MemberDto.FromEntity(member);
    }
}
