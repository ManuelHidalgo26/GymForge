using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Classes;

// Las fechas de los horarios se manejan en hora local del gimnasio (no UTC):
// para una agenda semanal es lo intuitivo y evita conversiones en la UI.

public record ScheduleRowDto(
    Guid Id, Guid ClassDescriptionId, string ClassName,
    DateTime Start, DateTime End, int Capacity, int BookedCount)
{
    public int Available => Capacity - BookedCount;
    public bool IsFull => BookedCount >= Capacity;
    public string Occupancy => $"{BookedCount}/{Capacity}";

    public static ScheduleRowDto FromEntity(ClassSchedule s) => new(
        s.Id, s.ClassDescriptionId, s.ClassDescription?.Name ?? "Clase",
        s.StartDatetime, s.EndDatetime, s.Capacity,
        s.Bookings.Count(b => b.Status == BookingStatus.Booked));
}

public record BookingRowDto(Guid Id, string MemberName, BookingStatus Status, DateTime BookedAt);

// ── Horarios ─────────────────────────────────────────────────────────────────

/// <summary>Horarios de la sede en la semana que arranca en WeekStart (7 días).</summary>
public record GetWeekSchedulesQuery(Guid CompanyId, Guid SiteId, DateOnly WeekStart)
    : IRequest<IReadOnlyList<ScheduleRowDto>>;

public class GetWeekSchedulesQueryHandler : IRequestHandler<GetWeekSchedulesQuery, IReadOnlyList<ScheduleRowDto>>
{
    private readonly IClassRepository _repo;
    public GetWeekSchedulesQueryHandler(IClassRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<ScheduleRowDto>> Handle(GetWeekSchedulesQuery q, CancellationToken ct)
    {
        var from = q.WeekStart.ToDateTime(TimeOnly.MinValue);
        var to = q.WeekStart.AddDays(7).ToDateTime(TimeOnly.MinValue);
        var schedules = await _repo.GetSchedulesAsync(q.CompanyId, q.SiteId, from, to, ct);
        return schedules.Select(ScheduleRowDto.FromEntity).ToList();
    }
}

public record CreateScheduleCommand(
    Guid CompanyId, Guid SiteId, Guid ClassDescriptionId, DateTime Start, int Capacity)
    : IRequest<ScheduleRowDto>;

public class CreateScheduleCommandValidator : AbstractValidator<CreateScheduleCommand>
{
    public CreateScheduleCommandValidator()
    {
        RuleFor(x => x.ClassDescriptionId).NotEmpty().WithMessage("Elegí una clase.");
        RuleFor(x => x.Capacity).GreaterThan(0).WithMessage("El cupo debe ser positivo.");
    }
}

public class CreateScheduleCommandHandler : IRequestHandler<CreateScheduleCommand, ScheduleRowDto>
{
    private readonly IClassRepository _repo;
    public CreateScheduleCommandHandler(IClassRepository repo) => _repo = repo;

    public async Task<ScheduleRowDto> Handle(CreateScheduleCommand cmd, CancellationToken ct)
    {
        var cls = await _repo.GetClassAsync(cmd.ClassDescriptionId, ct)
            ?? throw new InvalidOperationException("La clase seleccionada no existe.");

        var end = cmd.Start.AddMinutes(cls.DefaultDurationMin);
        var schedule = ClassSchedule.Create(cmd.CompanyId, cmd.SiteId, cls.Id, cmd.Start, end, cmd.Capacity);

        await _repo.AddScheduleAsync(schedule, ct);
        await _repo.SaveChangesAsync(ct);

        return new ScheduleRowDto(schedule.Id, cls.Id, cls.Name, cmd.Start, end, cmd.Capacity, 0);
    }
}

// ── Reservas ─────────────────────────────────────────────────────────────────

public record GetScheduleBookingsQuery(Guid CompanyId, Guid ScheduleId)
    : IRequest<IReadOnlyList<BookingRowDto>>;

public class GetScheduleBookingsQueryHandler
    : IRequestHandler<GetScheduleBookingsQuery, IReadOnlyList<BookingRowDto>>
{
    private readonly IClassRepository _repo;
    public GetScheduleBookingsQueryHandler(IClassRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<BookingRowDto>> Handle(GetScheduleBookingsQuery q, CancellationToken ct)
    {
        var schedule = await _repo.GetScheduleAsync(q.ScheduleId, ct);
        if (schedule is null || schedule.CompanyId != q.CompanyId)
            return [];

        return schedule.Bookings
            .Where(b => b.Status is BookingStatus.Booked or BookingStatus.Attended)
            .OrderBy(b => b.BookedAt)
            .Select(b => new BookingRowDto(b.Id, b.Member.FullName, b.Status, b.BookedAt))
            .ToList();
    }
}

public record BookMemberCommand(Guid CompanyId, Guid ScheduleId, Guid MemberId) : IRequest<BookingRowDto>;

public class BookMemberCommandValidator : AbstractValidator<BookMemberCommand>
{
    public BookMemberCommandValidator()
    {
        RuleFor(x => x.ScheduleId).NotEmpty().WithMessage("Elegí un horario.");
        RuleFor(x => x.MemberId).NotEmpty().WithMessage("Elegí un socio.");
    }
}

public class BookMemberCommandHandler : IRequestHandler<BookMemberCommand, BookingRowDto>
{
    private readonly IClassRepository _repo;
    private readonly IMemberRepository _members;

    public BookMemberCommandHandler(IClassRepository repo, IMemberRepository members)
    {
        _repo = repo;
        _members = members;
    }

    public async Task<BookingRowDto> Handle(BookMemberCommand cmd, CancellationToken ct)
    {
        var schedule = await _repo.GetScheduleAsync(cmd.ScheduleId, ct);
        if (schedule is null || schedule.CompanyId != cmd.CompanyId)
            throw new InvalidOperationException("El horario seleccionado no existe.");

        var member = await _members.GetByIdAsync(cmd.MemberId, ct)
            ?? throw new InvalidOperationException("El socio seleccionado no existe.");

        if (schedule.Bookings.Any(b => b.MemberId == cmd.MemberId && b.Status == BookingStatus.Booked))
            throw new InvalidOperationException("El socio ya tiene una reserva en esta clase.");

        if (schedule.IsFull)
            throw new InvalidOperationException("La clase está completa.");

        var booking = Booking.Create(cmd.CompanyId, cmd.MemberId, cmd.ScheduleId);
        await _repo.AddBookingAsync(booking, ct);
        await _repo.SaveChangesAsync(ct);

        return new BookingRowDto(booking.Id, member.FullName, booking.Status, booking.BookedAt);
    }
}

public record CancelBookingCommand(Guid CompanyId, Guid BookingId) : IRequest;

public class CancelBookingCommandHandler : IRequestHandler<CancelBookingCommand>
{
    private readonly IClassRepository _repo;
    public CancelBookingCommandHandler(IClassRepository repo) => _repo = repo;

    public async Task Handle(CancelBookingCommand cmd, CancellationToken ct)
    {
        var booking = await _repo.GetBookingAsync(cmd.BookingId, ct);
        if (booking is null || booking.CompanyId != cmd.CompanyId)
            throw new InvalidOperationException("La reserva no existe.");

        booking.Cancel();
        await _repo.SaveChangesAsync(ct);
    }
}
