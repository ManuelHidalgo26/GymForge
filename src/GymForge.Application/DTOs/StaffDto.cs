using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.DTOs;

public record StaffDto(Guid Id, string FullName, StaffRole Role, string ColorHex)
{
    public static StaffDto FromEntity(Staff s) => new(s.Id, s.FullName, s.Role, s.ColorHex);
}
