using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Access;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class GetTodaysAccessLogQueryHandlerTests
{
    private readonly IAccessLogRepository _logs = Substitute.For<IAccessLogRepository>();
    private readonly ISiteRepository _sites = Substitute.For<ISiteRepository>();
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();
    private static readonly DateOnly Today = new(2026, 7, 21);

    private GetTodaysAccessLogQueryHandler Sut(Company company)
    {
        _sites.GetCompanyAsync(company.Id, Arg.Any<CancellationToken>()).Returns(company);
        _calendar.TodayFor(company).Returns(Today);
        return new GetTodaysAccessLogQueryHandler(_logs, _sites, _calendar);
    }

    [Fact]
    public async Task Handle_PideElDiaLocalDeLaEmpresaConvertidoAUtc()
    {
        var company = Company.Create("Power Gym", "30-71234567-3");   // Argentina: UTC-3
        var siteId = Guid.NewGuid();
        _logs.GetBySiteAsync(siteId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AccessLog>());

        await Sut(company).Handle(new GetTodaysAccessLogQuery(company.Id, siteId), CancellationToken.None);

        await _logs.Received(1).GetBySiteAsync(
            siteId, new DateTime(2026, 7, 21, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 22, 3, 0, 0, DateTimeKind.Utc), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MapsRows_WithMemberNameAndStatusText()
    {
        var company = Company.Create("Power Gym", "30-71234567-3");
        var companyId = company.Id;
        var siteId = Guid.NewGuid();
        var member = Member.Create(companyId, siteId, "Juan", "Pérez", DocumentType.DNI, "30111222", Gender.Male);

        var granted = AccessLog.Granted(companyId, siteId, member.Id, null, 1, AccessMethod.Manual, AccessDirection.In);
        SetMember(granted, member);
        var denied = AccessLog.Denied(companyId, siteId, member.Id, 1, AccessMethod.Manual, AccessDenialReason.IncompleteMembership);
        SetMember(denied, member);

        _logs.GetBySiteAsync(siteId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { granted, denied });

        var rows = await Sut(company).Handle(
            new GetTodaysAccessLogQuery(companyId, siteId), CancellationToken.None);

        rows.Should().HaveCount(2);
        rows.Should().Contain(r => r.MemberName == "Juan Pérez" && r.Granted && r.StatusText == "Ingresó");
        rows.Should().Contain(r => !r.Granted && r.StatusText == "Sin membresía activa");
    }

    private static void SetMember(AccessLog log, Member m) =>
        typeof(AccessLog).GetProperty(nameof(AccessLog.Member))!.SetValue(log, m);
}
