namespace GymForge.Infrastructure.Persistence;

/// <summary>
/// Usuario actual fijado explícitamente con <see cref="Set"/>: lo usan la API, el seed y los tests.
/// Sin <see cref="Set"/> no hay usuario (null = operación de sistema).
/// </summary>
public sealed class AmbientCurrentUser : ScopedCurrentUser
{
    private Guid? _staffId;

    protected override Guid? BaseStaffId => _staffId;

    public void Set(Guid? staffId) => _staffId = staffId;
}
