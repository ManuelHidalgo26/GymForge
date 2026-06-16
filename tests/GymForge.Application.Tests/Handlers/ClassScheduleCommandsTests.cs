using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Classes;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class ClassScheduleCommandsTests
{
    private readonly IClassRepository _repo = Substitute.For<IClassRepository>();
    private readonly IMemberRepository _members = Substitute.For<IMemberRepository>();

    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _siteId = Guid.NewGuid();

    [Fact]
    public async Task CreateSchedule_UsesClassDuration_AndPersists()
    {
        var cls = ClassDescription.Create(_companyId, "Funcional", durationMin: 45, capacity: 20);
        _repo.GetClassAsync(cls.Id, Arg.Any<CancellationToken>()).Returns(cls);

        var start = new DateTime(2026, 6, 17, 18, 0, 0);
        var dto = await new CreateScheduleCommandHandler(_repo).Handle(
            new CreateScheduleCommand(_companyId, _siteId, cls.Id, start, Capacity: 10),
            CancellationToken.None);

        dto.End.Should().Be(start.AddMinutes(45));
        dto.Capacity.Should().Be(10);
        dto.BookedCount.Should().Be(0);
        await _repo.Received(1).AddScheduleAsync(Arg.Any<ClassSchedule>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BookMember_WhenFull_Throws()
    {
        var schedule = ClassSchedule.Create(_companyId, _siteId, Guid.NewGuid(),
            new DateTime(2026, 6, 17, 18, 0, 0), new DateTime(2026, 6, 17, 19, 0, 0), capacity: 1);
        schedule.Bookings.Add(Booking.Create(_companyId, Guid.NewGuid(), schedule.Id));
        _repo.GetScheduleAsync(schedule.Id, Arg.Any<CancellationToken>()).Returns(schedule);
        _members.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(SampleMember());

        var act = () => new BookMemberCommandHandler(_repo, _members).Handle(
            new BookMemberCommand(_companyId, schedule.Id, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*completa*");
    }

    [Fact]
    public async Task BookMember_Duplicate_Throws()
    {
        var memberId = Guid.NewGuid();
        var schedule = ClassSchedule.Create(_companyId, _siteId, Guid.NewGuid(),
            new DateTime(2026, 6, 17, 18, 0, 0), new DateTime(2026, 6, 17, 19, 0, 0), capacity: 10);
        schedule.Bookings.Add(Booking.Create(_companyId, memberId, schedule.Id));
        _repo.GetScheduleAsync(schedule.Id, Arg.Any<CancellationToken>()).Returns(schedule);
        _members.GetByIdAsync(memberId, Arg.Any<CancellationToken>()).Returns(SampleMember());

        var act = () => new BookMemberCommandHandler(_repo, _members).Handle(
            new BookMemberCommand(_companyId, schedule.Id, memberId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ya tiene una reserva*");
    }

    [Fact]
    public async Task BookMember_WithCapacity_CreatesBooking()
    {
        var schedule = ClassSchedule.Create(_companyId, _siteId, Guid.NewGuid(),
            new DateTime(2026, 6, 17, 18, 0, 0), new DateTime(2026, 6, 17, 19, 0, 0), capacity: 10);
        _repo.GetScheduleAsync(schedule.Id, Arg.Any<CancellationToken>()).Returns(schedule);
        var member = SampleMember();
        _members.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);

        var dto = await new BookMemberCommandHandler(_repo, _members).Handle(
            new BookMemberCommand(_companyId, schedule.Id, member.Id), CancellationToken.None);

        dto.MemberName.Should().Be(member.FullName);
        dto.Status.Should().Be(BookingStatus.Booked);
        await _repo.Received(1).AddBookingAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelBooking_SetsStatusCancelled()
    {
        var booking = Booking.Create(_companyId, Guid.NewGuid(), Guid.NewGuid());
        _repo.GetBookingAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        await new CancelBookingCommandHandler(_repo).Handle(
            new CancelBookingCommand(_companyId, booking.Id), CancellationToken.None);

        booking.Status.Should().Be(BookingStatus.Cancelled);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private Member SampleMember() =>
        Member.Create(_companyId, _siteId, "Ana", "Lopez", DocumentType.DNI, "30111222", Gender.Female);
}
