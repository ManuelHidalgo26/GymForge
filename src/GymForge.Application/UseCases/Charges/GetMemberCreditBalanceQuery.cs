using GymForge.Application.Interfaces;
using MediatR;

namespace GymForge.Application.UseCases.Charges;

/// <summary>Saldo a favor del socio (para la ficha). Filtrado por el tenant activo.</summary>
public record GetMemberCreditBalanceQuery(Guid MemberId) : IRequest<decimal>;

public class GetMemberCreditBalanceQueryHandler : IRequestHandler<GetMemberCreditBalanceQuery, decimal>
{
    private readonly IMemberCreditRepository _creditRepo;
    public GetMemberCreditBalanceQueryHandler(IMemberCreditRepository creditRepo) => _creditRepo = creditRepo;

    public Task<decimal> Handle(GetMemberCreditBalanceQuery query, CancellationToken ct) =>
        _creditRepo.GetBalanceAsync(query.MemberId, ct);
}
