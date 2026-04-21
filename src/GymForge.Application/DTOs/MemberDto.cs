using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.DTOs;

public record MemberDto(
    Guid Id,
    Guid SiteId,
    string FirstName,
    string LastName,
    string FullName,
    DocumentType DocumentType,
    string DocumentNumber,
    string? Email,
    string? Mobile,
    DateOnly? BirthDate,
    int? Age,
    Gender Gender,
    MemberStatus Status,
    string? PhotoUrl,
    string? TagSerial,
    DateOnly? JoinDate,
    bool HasFingerprint,
    DateTime CreatedAt)
{
    public static MemberDto FromEntity(Member m) => new(
        m.Id, m.SiteId, m.FirstName, m.LastName, m.FullName,
        m.DocumentType, m.DocumentNumber, m.Email, m.Mobile,
        m.BirthDate, m.Age, m.Gender, m.Status, m.PhotoUrl,
        m.TagSerial, m.JoinDate,
        m.FingerprintTemplate is { Length: > 0 },
        m.CreatedAt);
}

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
