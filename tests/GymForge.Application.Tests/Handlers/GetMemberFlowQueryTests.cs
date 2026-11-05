using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Reports;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

/// <summary>Altas y bajas del período más la foto de estados de hoy (ADR-007, Pieza E).</summary>
public class GetMemberFlowQueryTests
{
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 3);

    private readonly IMemberRepository _members = Substitute.For<IMemberRepository>();
    private readonly IMembershipRepository _memberships = Substitute.For<IMembershipRepository>();
    private readonly ISiteRepository _sites = Substitute.For<ISiteRepository>();
    private readonly Company _company = Company.Create("Power Gym", "30-71234567-3");
    private readonly Guid _companyId;
    private readonly Guid _siteId = Guid.NewGuid();

    public GetMemberFlowQueryTests()
    {
        _companyId = _company.Id;
        _sites.GetCompanyAsync(_companyId, Arg.Any<CancellationToken>()).Returns(_company);
        HaveStatusCounts(new Dictionary<MemberStatus, int>());
        HaveJoined();
        HaveCancelled();
    }

    private void HaveStatusCounts(IReadOnlyDictionary<MemberStatus, int> counts) =>
        _members.CountByStatusAsync(_companyId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(counts);

    private void HaveJoined(params Member[] joined) =>
        _members.GetJoinedBetweenAsync(
                _companyId, Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(joined.ToList());

    private void HaveCancelled(params Membership[] cancelled) =>
        _memberships.GetCancelledBetweenAsync(
                _companyId, Arg.Any<Guid?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(cancelled.ToList());

    private Task<MemberFlow> RunAsync(Guid? siteId = null) =>
        new GetMemberFlowQueryHandler(_members, _memberships, _sites).Handle(
            new GetMemberFlowQuery(_companyId, siteId, From, To), CancellationToken.None);

    private Member Joined(string firstName, string dni, DateOnly joinDate)
    {
        var member = Member.Create(_companyId, _siteId, firstName, "García", DocumentType.DNI, dni, Gender.Female);
        member.Activate(joinDate);
        return member;
    }

    private Membership Cancelled(string firstName, string plan, DateTime cancelDate, string? reason)
    {
        var member = Member.Create(_companyId, _siteId, firstName, "Paz", DocumentType.DNI, "30111222", Gender.Male);
        var type = MembershipType.Create(_companyId, plan, MembershipBasis.Renewal, 35_000m);
        var membership = Membership.Create(_companyId, _siteId, member.Id, type.Id, new DateOnly(2026, 6, 1), null);
        membership.Cancel(reason ?? string.Empty);
        Set(membership, nameof(Membership.CancelDate), cancelDate);
        Set(membership, nameof(Membership.CancelReason), reason);
        Set(membership, nameof(Membership.Member), member);
        Set(membership, nameof(Membership.MembershipType), type);
        return membership;
    }

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    // ── Foto de estados ──────────────────────────────────────────────────────

    [Fact]
    public async Task StatusCounts_SonLaFotoDeHoy_YLosActivosSalenDeAhi()
    {
        HaveStatusCounts(new Dictionary<MemberStatus, int>
        {
            [MemberStatus.Active] = 120, [MemberStatus.Frozen] = 7, [MemberStatus.Overdue] = 11,
        });

        var flow = await RunAsync();

        flow.ActiveNow.Should().Be(120);
        flow.StatusCounts[MemberStatus.Frozen].Should().Be(7);
        flow.StatusCounts[MemberStatus.Overdue].Should().Be(11);
    }

    [Fact]
    public async Task SinSociosActivos_ActiveNowEsCero()
    {
        var flow = await RunAsync();

        flow.ActiveNow.Should().Be(0);
    }

    // ── Altas ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Joins_TraenNombreDocumentoYFechaDeAlta_OrdenadosPorFecha()
    {
        HaveJoined(
            Joined("Carla", "30333444", new DateOnly(2026, 11, 3)),
            Joined("Ana", "30111222", new DateOnly(2026, 11, 1)),
            Joined("Beto", "30222333", new DateOnly(2026, 11, 2)));

        var flow = await RunAsync();

        flow.Joins.Should().Equal(
            new MemberFlowJoin("Ana García", "30111222", new DateOnly(2026, 11, 1)),
            new MemberFlowJoin("Beto García", "30222333", new DateOnly(2026, 11, 2)),
            new MemberFlowJoin("Carla García", "30333444", new DateOnly(2026, 11, 3)));
    }

    [Fact]
    public async Task Joins_ElRangoDeFechasViajaTalCualAlRepositorio_ConAmbosExtremos()
    {
        await RunAsync();

        await _members.Received(1).GetJoinedBetweenAsync(
            _companyId, null, From, To, Arg.Any<CancellationToken>());
    }

    // ── Bajas ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Leaves_TraenPlanFechaYMotivo_OrdenadosPorFechaDeBaja()
    {
        HaveCancelled(
            Cancelled("Luis", "Trimestral", new DateTime(2026, 11, 3, 23, 59, 0), "Se mudó"),
            Cancelled("Marta", "Mensual Full", new DateTime(2026, 11, 1, 12, 0, 0), reason: null));

        var flow = await RunAsync();

        flow.Leaves.Should().Equal(
            new MemberFlowLeave("Marta Paz", "Mensual Full", new DateOnly(2026, 11, 1), null),
            new MemberFlowLeave("Luis Paz", "Trimestral", new DateOnly(2026, 11, 3), "Se mudó"));
    }

    [Fact]
    public async Task Leaves_ElRangoSePideEnUtcSobreLosDiasLocalesDelGimnasio()
    {
        await RunAsync();

        // Del 01/11 a las 00:00 al 04/11 a las 00:00 en Argentina (UTC-3).
        await _memberships.Received(1).GetCancelledBetweenAsync(
            _companyId, null,
            new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 4, 3, 0, 0, DateTimeKind.Utc),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leaves_UnaBajaDeLas2330LocalesFiguraConLaFechaDelGimnasio_NoLaUtc()
    {
        // 23:30 del 03/11 en Argentina es 02:30 UTC del 04/11.
        HaveCancelled(Cancelled("Luis", "Trimestral", new DateTime(2026, 11, 4, 2, 30, 0, DateTimeKind.Utc), "Se mudó"));

        var flow = await RunAsync();

        flow.Leaves.Should().ContainSingle().Which.CancelDate.Should().Be(new DateOnly(2026, 11, 3));
    }

    // ── Neto y alcance ───────────────────────────────────────────────────────

    [Fact]
    public async Task Net_EsAltasMenosBajas_YPuedeSerNegativo()
    {
        HaveJoined(Joined("Ana", "30111222", From));
        HaveCancelled(
            Cancelled("Luis", "Trimestral", new DateTime(2026, 11, 2), "x"),
            Cancelled("Marta", "Mensual Full", new DateTime(2026, 11, 2), "y"),
            Cancelled("Nico", "Mensual Full", new DateTime(2026, 11, 3), "z"));

        var flow = await RunAsync();

        flow.Net.Should().Be(-2);
    }

    [Fact]
    public async Task SiteId_ViajaALosTresRepositorios_ParaFiltrarUnaSedeOTodaLaEmpresa()
    {
        await RunAsync(_siteId);
        await RunAsync(siteId: null);

        await _members.Received(1).CountByStatusAsync(_companyId, _siteId, Arg.Any<CancellationToken>());
        await _members.Received(1).CountByStatusAsync(_companyId, null, Arg.Any<CancellationToken>());
        await _members.Received(1).GetJoinedBetweenAsync(
            _companyId, _siteId, From, To, Arg.Any<CancellationToken>());
        await _memberships.Received(1).GetCancelledBetweenAsync(
            _companyId, _siteId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
