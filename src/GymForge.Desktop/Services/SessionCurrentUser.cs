using GymForge.Infrastructure.Persistence;

namespace GymForge.Desktop.Services;

/// <summary>
/// "Quién" de la auditoría en el escritorio: el cajero que firmó sesión, leído en vivo. Es honesto: sin
/// sesión devuelve null en vez de inventar un usuario (la operación queda registrada como de sistema).
/// El mantenimiento diario corre dentro de <see cref="ScopedCurrentUser.BeginSystemScope"/> para no
/// atribuirle al cajero de turno lo que hizo el sistema.
/// </summary>
public sealed class SessionCurrentUser(SessionContext session) : ScopedCurrentUser
{
    protected override Guid? BaseStaffId => session.CashierId;
}
