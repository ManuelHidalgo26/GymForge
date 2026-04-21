using GymForge.Application.Interfaces;

namespace GymForge.Infrastructure.Services;

public class SystemClock : IClock
{
    public DateTime Now => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
