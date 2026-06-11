using GymForge.Application.Interfaces;
using MediatR;

namespace GymForge.Application.UseCases.Members;

/// <summary>
/// Elimina definitivamente un socio SIN movimientos (caso típico: alta de prueba
/// o por error). Si tiene membresías/cobros/pagos/accesos no se borra — el
/// historial es intocable — y se indica darlo de baja en su lugar.
/// </summary>
public record DeleteMemberCommand(Guid MemberId) : IRequest;

public class DeleteMemberCommandHandler : IRequestHandler<DeleteMemberCommand>
{
    private readonly IMemberRepository _repo;
    public DeleteMemberCommandHandler(IMemberRepository repo) => _repo = repo;

    public async Task Handle(DeleteMemberCommand cmd, CancellationToken ct)
    {
        var member = await _repo.GetByIdAsync(cmd.MemberId, ct)
            ?? throw new InvalidOperationException("El socio no existe.");

        if (await _repo.HasActivityAsync(cmd.MemberId, ct))
            throw new InvalidOperationException(
                "El socio tiene movimientos registrados (membresías, cobros o accesos) y no se puede eliminar. " +
                "Dale de baja desde su ficha para conservar el historial.");

        _repo.Remove(member);
        await _repo.SaveChangesAsync(ct);
    }
}
