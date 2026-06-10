using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using MediatR;

namespace GymForge.Application.UseCases.Catalog;

// ── Planes de membresía activos de la company ─────────────────────────────────

public record GetMembershipTypesQuery(Guid CompanyId) : IRequest<IReadOnlyList<MembershipTypeDto>>;

public class GetMembershipTypesQueryHandler
    : IRequestHandler<GetMembershipTypesQuery, IReadOnlyList<MembershipTypeDto>>
{
    private readonly IMembershipTypeRepository _repo;
    public GetMembershipTypesQueryHandler(IMembershipTypeRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<MembershipTypeDto>> Handle(GetMembershipTypesQuery q, CancellationToken ct) =>
        (await _repo.GetByCompanyAsync(q.CompanyId, ct)).Select(MembershipTypeDto.FromEntity).ToList();
}

// ── Productos activos de la company ───────────────────────────────────────────

public record GetProductsQuery(Guid CompanyId) : IRequest<IReadOnlyList<ProductDto>>;

public class GetProductsQueryHandler
    : IRequestHandler<GetProductsQuery, IReadOnlyList<ProductDto>>
{
    private readonly IProductRepository _repo;
    public GetProductsQueryHandler(IProductRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<ProductDto>> Handle(GetProductsQuery q, CancellationToken ct) =>
        (await _repo.GetByCompanyAsync(q.CompanyId, ct)).Select(ProductDto.FromEntity).ToList();
}
