using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using MediatR;

namespace GymForge.Application.UseCases.Staff;

/// <summary>Login de cajero por PIN: busca el staff activo de la company cuyo PIN coincide.</summary>
public record AuthenticateStaffCommand(Guid CompanyId, string Pin) : IRequest<StaffDto?>;

public class AuthenticateStaffCommandHandler : IRequestHandler<AuthenticateStaffCommand, StaffDto?>
{
    private readonly IStaffRepository _repo;
    private readonly IPinHasher _hasher;

    public AuthenticateStaffCommandHandler(IStaffRepository repo, IPinHasher hasher)
    {
        _repo = repo;
        _hasher = hasher;
    }

    public async Task<StaffDto?> Handle(AuthenticateStaffCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Pin)) return null;

        var staff = await _repo.GetActiveByCompanyAsync(cmd.CompanyId, ct);
        var match = staff.FirstOrDefault(s => _hasher.Verify(cmd.Pin, s.PinCodeHash));
        return match is null ? null : StaffDto.FromEntity(match);
    }
}
