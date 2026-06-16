using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class GetTodaysAccessLogQueryHandlerTests
{
    private readonly IAccessLogRepository _logs = Substitute.For<IAccessLogRepository>();

    [Fact]
    public async Task Handle_MapsRows_WithMemberNameAndStatusText()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var member = Member.Create(companyId, siteId, "Juan", "Pérez", DocumentType.DNI, "30111222", Gender.Male);

        var granted = AccessLog.Granted(companyId, siteId, member.Id, null, 1, AccessMethod.Manual, AccessDirection.In);
        SetMember(granted, member);
        var denied = AccessLog.Denied(companyId, siteId, member.Id, 1, AccessMethod.Manual, AccessDenialReason.IncompleteMembership);
        SetMember(denied, member);

        _logs.GetBySiteAsync(siteId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { granted, denied });

        var rows = await new GetTodaysAccessLogQueryHandler(_logs).Handle(
            new GetTodaysAccessLogQuery(companyId, siteId), CancellationToken.None);

        rows.Should().HaveCount(2);
        rows.Should().Contain(r => r.MemberName == "Juan Pérez" && r.Granted && r.StatusText == "Ingresó");
        rows.Should().Contain(r => !r.Granted && r.StatusText == "Sin membresía activa");
    }

    private static void SetMember(AccessLog log, Member m) =>
        typeof(AccessLog).GetProperty(nameof(AccessLog.Member))!.SetValue(log, m);
}
