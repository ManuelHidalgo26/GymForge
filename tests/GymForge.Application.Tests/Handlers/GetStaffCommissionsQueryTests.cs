using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Reports;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

/// <summary>Reporte "Comisiones del staff": suma los montos congelados al vender, por persona (ADR-008, Pieza E).</summary>
public class GetStaffCommissionsQueryTests
{
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 3);

    private readonly IMembershipRepository _memberships = Substitute.For<IMembershipRepository>();
    private readonly ISaleRepository _sales = Substitute.For<ISaleRepository>();
    private readonly IStaffRepository _staff = Substitute.For<IStaffRepository>();
    private readonly ISiteRepository _sites = Substitute.For<ISiteRepository>();
    private readonly Company _company = Company.Create("Power Gym", "30-71234567-3");
    private readonly Staff _ana;
    private readonly Staff _beto;

    public GetStaffCommissionsQueryTests()
    {
        _ana = Staff.Create(_company.Id, "Ana", "Paz", StaffRole.Receptionist, "hash");
        _beto = Staff.Create(_company.Id, "Beto", "Ruiz", StaffRole.SalesRep, "hash");
        _staff.GetAllByCompanyAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(new List<Staff> { _ana, _beto });
        _sites.GetCompanyAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(_company);
        HaveSold(null);
        HaveLines(null);
    }

    private Membership Sold(Staff seller, decimal commission)
    {
        var membership = Membership.Create(_company.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 11, 1), null);
        membership.SetCommission(seller.Id, commission);
        return membership;
    }

    private SaleLine Line(Guid staffId, decimal commission)
    {
        var line = SaleLine.Create(Guid.NewGuid(), "Barra", 1, 1_000m);
        line.SetCommission(staffId, commission);
        return line;
    }

    private void HaveSold(Guid? siteId, params Membership[] memberships) =>
        _memberships.GetSoldBetweenAsync(_company.Id, siteId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(memberships.ToList());

    private void HaveLines(Guid? siteId, params SaleLine[] lines) =>
        _sales.GetCommissionLinesBetweenAsync(_company.Id, siteId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(lines.ToList());

    private Task<StaffCommissions> RunAsync(Guid? siteId = null) =>
        new GetStaffCommissionsQueryHandler(_memberships, _sales, _staff, _sites).Handle(
            new GetStaffCommissionsQuery(_company.Id, siteId, From, To), CancellationToken.None);

    [Fact]
    public async Task GroupsAndAddsPerStaff_MembershipsAndProductsSeparately_OrderedByTotalDescending()
    {
        HaveSold(null, Sold(_ana, 3_500m), Sold(_ana, 1_500m), Sold(_beto, 2_000m));
        HaveLines(null, Line(_ana.Id, 300m), Line(_beto.Id, 4_000m), Line(_beto.Id, 200m));

        var result = await RunAsync();

        result.Rows.Should().Equal(
            new StaffCommissionRow(_beto.Id, "Beto Ruiz", 2_000m, 4_200m, 6_200m),
            new StaffCommissionRow(_ana.Id, "Ana Paz", 5_000m, 300m, 5_300m));
        result.Total.Should().Be(11_500m);
    }

    [Fact]
    public async Task Reconciles_MembershipsPlusProductsEqualsTheSumOfRowsEqualsTheTotal()
    {
        HaveSold(null, Sold(_ana, 925.88m), Sold(_beto, 100.05m), Sold(_ana, 0.07m));
        HaveLines(null, Line(_ana.Id, 12.34m), Line(_beto.Id, 56.78m));

        var result = await RunAsync();

        var memberships = result.Rows.Sum(r => r.MembershipCommission);
        var products = result.Rows.Sum(r => r.ProductCommission);
        (memberships + products).Should().Be(result.Rows.Sum(r => r.Total)).And.Be(result.Total);
        result.Rows.Should().OnlyContain(r => r.Total == r.MembershipCommission + r.ProductCommission);
        memberships.Should().Be(result.MembershipTotal);
        products.Should().Be(result.ProductTotal);
    }

    [Fact]
    public async Task APersonWithOnlyProducts_OrOnlyMemberships_AppearsWithZeroInTheOtherColumn()
    {
        HaveSold(null, Sold(_ana, 1_000m));
        HaveLines(null, Line(_beto.Id, 500m));

        var result = await RunAsync();

        result.Rows.Should().Contain(new StaffCommissionRow(_ana.Id, "Ana Paz", 1_000m, 0m, 1_000m));
        result.Rows.Should().Contain(new StaffCommissionRow(_beto.Id, "Beto Ruiz", 0m, 500m, 500m));
    }

    [Fact]
    public async Task NothingSold_ReturnsAnEmptyReportWithZeroTotal()
    {
        var result = await RunAsync();

        result.Rows.Should().BeEmpty();
        result.Total.Should().Be(0m);
    }

    [Fact]
    public async Task AskedWithTheDaysOfTheCompany_ConvertedToUtcBounds_InclusiveOfTheLastDay()
    {
        await RunAsync();

        // Argentina es UTC-3: el día 01/11 empieza a las 03:00 UTC y el 03/11 termina (exclusivo) el 04/11 a las 03:00 UTC.
        var start = new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);
        var endExclusive = new DateTime(2026, 11, 4, 3, 0, 0, DateTimeKind.Utc);
        await _memberships.Received(1).GetSoldBetweenAsync(
            _company.Id, null, start, endExclusive, Arg.Any<CancellationToken>());
        await _sales.Received(1).GetCommissionLinesBetweenAsync(
            _company.Id, null, start, endExclusive, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScopedToASite_AsksTheRepositoriesForThatSiteOnly()
    {
        var siteId = Guid.NewGuid();
        HaveSold(siteId, Sold(_ana, 1_000m));
        HaveSold(null, Sold(_beto, 9_999m));
        HaveLines(siteId);

        var result = await RunAsync(siteId);

        result.Rows.Should().ContainSingle().Which.StaffId.Should().Be(_ana.Id);
        await _memberships.DidNotReceive().GetSoldBetweenAsync(
            _company.Id, null, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnlyAsksForTheCompanyOfTheQuery_NeverAnother()
    {
        await RunAsync();

        await _memberships.DidNotReceive().GetSoldBetweenAsync(
            Arg.Is<Guid>(id => id != _company.Id), Arg.Any<Guid?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _staff.DidNotReceive().GetAllByCompanyAsync(
            Arg.Is<Guid>(id => id != _company.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SellerNoLongerInTheStaffList_StillShowsWithAGenericName_SoNothingIsLost()
    {
        var ghost = Guid.NewGuid();
        HaveLines(null, Line(ghost, 700m));

        var result = await RunAsync();

        result.Rows.Should().ContainSingle()
            .Which.Should().Be(new StaffCommissionRow(ghost, "Personal no identificado", 0m, 700m, 700m));
        result.Total.Should().Be(700m);
    }
}
