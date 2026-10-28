namespace GymForge.Application.Interfaces;

/// <summary>Staff responsable de la operación en curso. Null = sistema (p. ej. el mantenimiento diario).</summary>
public interface ICurrentUser
{
    Guid? StaffId { get; }
}
