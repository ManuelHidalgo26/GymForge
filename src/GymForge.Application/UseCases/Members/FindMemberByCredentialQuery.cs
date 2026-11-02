using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using MediatR;

namespace GymForge.Application.UseCases.Members;

/// <summary>
/// Resuelve a un socio el código leído por un scanner (credencial del gimnasio o DNI), con la misma regla que
/// usa el Gatekeeper para <c>AccessMethod.Barcode</c>. <c>null</c> si el código no corresponde a nadie.
/// </summary>
public record FindMemberByCredentialQuery(Guid CompanyId, string Code) : IRequest<MemberDto?>;

public class FindMemberByCredentialQueryHandler : IRequestHandler<FindMemberByCredentialQuery, MemberDto?>
{
    private readonly IMemberRepository _repo;
    public FindMemberByCredentialQueryHandler(IMemberRepository repo) => _repo = repo;

    public async Task<MemberDto?> Handle(FindMemberByCredentialQuery q, CancellationToken ct)
    {
        var member = await CredentialMatch.FindMemberAsync(_repo, q.Code, q.CompanyId, ct);
        return member is null ? null : MemberDto.FromEntity(member);
    }
}
