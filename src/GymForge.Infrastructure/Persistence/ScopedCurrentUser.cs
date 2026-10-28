using GymForge.Application.Interfaces;

namespace GymForge.Infrastructure.Persistence;

/// <summary>
/// Base de los proveedores de usuario: resuelve el usuario base (sesión, API, tests) y permite marcar un
/// tramo como operación de sistema con <see cref="BeginSystemScope"/> (p. ej. el mantenimiento diario, que
/// aunque corra con un cajero logueado no es obra suyo). Vive en un AsyncLocal, igual que el scope de tenant,
/// así que no se filtra a otros flujos concurrentes (la UI mientras corre el mantenimiento).
/// </summary>
public abstract class ScopedCurrentUser : ICurrentUser
{
    private readonly AsyncLocal<bool> _runningAsSystem = new();

    protected abstract Guid? BaseStaffId { get; }

    public Guid? StaffId => _runningAsSystem.Value ? null : BaseStaffId;

    /// <summary>Hasta disponer el scope, <see cref="StaffId"/> es null: lo que se guarde queda como del sistema.</summary>
    public IDisposable BeginSystemScope()
    {
        var previous = _runningAsSystem.Value;
        _runningAsSystem.Value = true;
        return new ScopeRestorer(_runningAsSystem, previous);
    }

    private sealed class ScopeRestorer(AsyncLocal<bool> runningAsSystem, bool previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            runningAsSystem.Value = previous;
        }
    }
}
