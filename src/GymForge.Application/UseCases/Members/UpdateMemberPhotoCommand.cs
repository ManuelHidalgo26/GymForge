using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using MediatR;

namespace GymForge.Application.UseCases.Members;

/// <summary>
/// Guarda la ruta de la foto del socio. El archivo ya lo escribió quien llama (la UI, como con el logo):
/// Application no hace I/O de disco, solo registra dónde quedó.
/// </summary>
public record UpdateMemberPhotoCommand(Guid CompanyId, Guid MemberId, string PhotoPath) : IRequest<MemberDto>;

public class UpdateMemberPhotoCommandValidator : AbstractValidator<UpdateMemberPhotoCommand>
{
    /// <summary>Largo máximo de la columna <c>Members.PhotoUrl</c>.</summary>
    private const int PhotoPathMaxLength = 500;

    public UpdateMemberPhotoCommandValidator()
    {
        RuleFor(x => x.PhotoPath)
            .NotEmpty().WithMessage("Falta la ruta de la foto.")
            .MaximumLength(PhotoPathMaxLength);
    }
}

public class UpdateMemberPhotoCommandHandler : IRequestHandler<UpdateMemberPhotoCommand, MemberDto>
{
    private readonly IMemberRepository _repo;
    public UpdateMemberPhotoCommandHandler(IMemberRepository repo) => _repo = repo;

    public async Task<MemberDto> Handle(UpdateMemberPhotoCommand cmd, CancellationToken ct)
    {
        var member = await _repo.GetByIdAsync(cmd.MemberId, ct);
        if (member is null || member.CompanyId != cmd.CompanyId)
            throw new KeyNotFoundException("El socio no existe.");

        member.UpdatePhoto(cmd.PhotoPath);
        _repo.Update(member);
        await _repo.SaveChangesAsync(ct);

        return MemberDto.FromEntity(member);
    }
}
