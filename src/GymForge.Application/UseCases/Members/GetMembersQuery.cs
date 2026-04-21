using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Members;

public record GetMembersQuery(
    Guid CompanyId,
    Guid SiteId,
    int Page = 1,
    int PageSize = 50,
    MemberStatus? Status = null) : IRequest<PagedResult<MemberDto>>;

public record SearchMembersQuery(
    string Query,
    Guid CompanyId,
    Guid SiteId,
    int Take = 20) : IRequest<IReadOnlyList<MemberDto>>;

public record GetMemberByIdQuery(Guid Id) : IRequest<MemberDto?>;

public class GetMembersQueryHandler : IRequestHandler<GetMembersQuery, PagedResult<MemberDto>>
{
    private readonly IMemberRepository _repo;
    public GetMembersQueryHandler(IMemberRepository repo) => _repo = repo;

    public async Task<PagedResult<MemberDto>> Handle(GetMembersQuery q, CancellationToken ct)
    {
        var members = await _repo.GetPagedAsync(q.CompanyId, q.SiteId, q.Page, q.PageSize, q.Status, ct);
        var total = await _repo.CountAsync(q.CompanyId, q.SiteId, q.Status, ct);

        return new PagedResult<MemberDto>(
            members.Select(MemberDto.FromEntity).ToList(),
            total,
            q.Page,
            q.PageSize);
    }
}

public class SearchMembersQueryHandler : IRequestHandler<SearchMembersQuery, IReadOnlyList<MemberDto>>
{
    private readonly IMemberRepository _repo;
    public SearchMembersQueryHandler(IMemberRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<MemberDto>> Handle(SearchMembersQuery q, CancellationToken ct)
    {
        var members = await _repo.SearchAsync(q.Query, q.CompanyId, q.SiteId, q.Take, ct);
        return members.Select(MemberDto.FromEntity).ToList();
    }
}

public class GetMemberByIdQueryHandler : IRequestHandler<GetMemberByIdQuery, MemberDto?>
{
    private readonly IMemberRepository _repo;
    public GetMemberByIdQueryHandler(IMemberRepository repo) => _repo = repo;

    public async Task<MemberDto?> Handle(GetMemberByIdQuery q, CancellationToken ct)
    {
        var m = await _repo.GetByIdAsync(q.Id, ct);
        return m is null ? null : MemberDto.FromEntity(m);
    }
}
