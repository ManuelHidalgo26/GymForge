using FluentAssertions;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Charges;
using GymForge.Application.UseCases.Reports;
using GymForge.Application.UseCases.Sales;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using GymForge.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>
/// Pruebas de qa sobre las comisiones (ADR-008): sin doble conteo al cobrar, snapshot inmune a cambios de % y
/// reporte que no cruza empresas ni sedes.
/// </summary>
public class CommissionQaTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private Company _company = null!;
    private Site _centro = null!;
    private Site _norte = null!;
    private Staff _seller = null!;
    private Member _member = null!;
    private MembershipType _plan = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);
        _company = Company.Create("Gym QA", "30-11111111-1");
        _centro = Site.Create(_company.Id, "Centro", "Calle 1");
        _norte = Site.Create(_company.Id, "Norte", "Calle 2");
        await _db.Companies.AddAsync(_company);
        await _db.Sites.AddRangeAsync(_centro, _norte);
        _seller = Staff.Create(_company.Id, "Caro", "Vendedora", StaffRole.Receptionist, "hash");
        _seller.SetCommissions(10m, 0m, 0m);
        _member = Member.Create(_company.Id, _centro.Id, "Ana", "Paz", DocumentType.DNI, "30111222", Gender.Female);
        _plan = MembershipType.Create(_company.Id, "Mensual", MembershipBasis.Renewal, 10_000m);
        await _db.Staff.AddAsync(_seller);
        await _db.Members.AddAsync(_member);
        await _db.MembershipTypes.AddAsync(_plan);
        await _db.SaveChangesAsync();
        _tenant.Set(_company.Id, _centro.Id);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private SellMembershipCommandHandler SellHandler() => new(
        new MembershipTypeRepository(_db), new ChargeRepository(_db), new MembershipRepository(_db),
        new MemberRepository(_db), new PaymentRepository(_db), Substitute.For<ICashRegister>(), new StaffRepository(_db),
        new CompanyCalendar(new SystemClock(), new SiteRepository(_db), NullLogger<CompanyCalendar>.Instance),
        TestServices.ArrearsRelief(_db, new SystemClock()));

    private async Task SellAsync() =>
        await SellHandler().Handle(
            new SellMembershipCommand(_company.Id, _centro.Id, _seller.Id, null, _member.Id, _plan.Id, PaymentMethod.Cash),
            CancellationToken.None);

    [Fact]
    public async Task SellMembership_ChangingTheSellerPercentageAfterwards_DoesNotRewriteTheFrozenCommission()
    {
        await SellAsync();
        _seller.SetCommissions(50m, 0m, 0m);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var membership = await _db.Memberships.SingleAsync();
        membership.CommissionAmount.Should().Be(1_000m, "10% de 10.000 congelado al vender");
        membership.SoldByStaffId.Should().Be(_seller.Id);
    }

    [Fact]
    public async Task ProcessPayment_OfALaterCharge_NeverTouchesTheFrozenCommission_NoDoubleCounting()
    {
        await SellAsync();
        var membership = await _db.Memberships.SingleAsync();
        var extra = Charge.Create(_company.Id, _centro.Id, _member.Id, membership.Id,
            ConceptType.MembershipFee, "Cuota 2", 10_000m, DateOnly.FromDateTime(DateTime.UtcNow));
        await _db.Charges.AddAsync(extra);
        await _db.SaveChangesAsync();

        await new ProcessPaymentCommandHandler(
                new ChargeRepository(_db), new PaymentRepository(_db), Substitute.For<ICashRegister>(),
                new MemberCreditRepository(_db), TestServices.ArrearsRelief(_db, new SystemClock()))
            .Handle(new ProcessPaymentCommand(
                _company.Id, _centro.Id, _member.Id, _seller.Id, 10_000m, PaymentMethod.Cash, ShiftId: null,
                ChargeIds: [extra.Id]), CancellationToken.None);
        _db.ChangeTracker.Clear();

        (await _db.Memberships.SingleAsync()).CommissionAmount.Should().Be(1_000m, "cobrar una cuota no suma comisión");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await new GetStaffCommissionsQueryHandler(
                new MembershipRepository(_db), new SaleRepository(_db), new StaffRepository(_db), new SiteRepository(_db))
            .Handle(new GetStaffCommissionsQuery(_company.Id, null, today.AddDays(-1), today.AddDays(1)), CancellationToken.None);
        report.Total.Should().Be(1_000m);
    }

    [Fact]
    public async Task StaffCommissionsReport_NeverIncludesAnotherCompanysSales_NorAnotherSiteWhenScoped()
    {
        await SellAsync();   // Centro, 1.000

        // Otra empresa con una venta propia.
        var other = Company.Create("Otro Gym", "30-22222222-2");
        var otherSite = Site.Create(other.Id, "Sede", "X 1");
        var otherSeller = Staff.Create(other.Id, "Eva", "Ajena", StaffRole.Receptionist, "hash");
        await _db.Companies.AddAsync(other);
        await _db.Sites.AddAsync(otherSite);
        await _db.Staff.AddAsync(otherSeller);
        var otherMember = Member.Create(other.Id, otherSite.Id, "Zoe", "Ajena", DocumentType.DNI, "1", Gender.Female);
        var otherPlan = MembershipType.Create(other.Id, "Plan", MembershipBasis.Renewal, 1_000m);
        var norteMember = Member.Create(_company.Id, _norte.Id, "Leo", "Norte", DocumentType.DNI, "2", Gender.Male);
        await _db.Members.AddRangeAsync(otherMember, norteMember);
        await _db.MembershipTypes.AddAsync(otherPlan);
        await _db.SaveChangesAsync();
        var foreign = Membership.Create(other.Id, otherSite.Id, otherMember.Id, otherPlan.Id,
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            soldByStaffId: otherSeller.Id);
        foreign.Activate();
        foreign.SetCommission(otherSeller.Id, 9_999m);
        await _db.Memberships.AddAsync(foreign);
        // Y una venta en otra sede de la misma empresa.
        var norte = Membership.Create(_company.Id, _norte.Id, norteMember.Id, _plan.Id,
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            soldByStaffId: _seller.Id);
        norte.Activate();
        norte.SetCommission(_seller.Id, 500m);
        await _db.Memberships.AddAsync(norte);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var handler = new GetStaffCommissionsQueryHandler(
            new MembershipRepository(_db), new SaleRepository(_db), new StaffRepository(_db), new SiteRepository(_db));

        var wholeCompany = await handler.Handle(
            new GetStaffCommissionsQuery(_company.Id, null, today.AddDays(-1), today.AddDays(1)), CancellationToken.None);
        var onlyCentro = await handler.Handle(
            new GetStaffCommissionsQuery(_company.Id, _centro.Id, today.AddDays(-1), today.AddDays(1)), CancellationToken.None);

        wholeCompany.Total.Should().Be(1_500m, "las dos sedes de la empresa, nunca la otra empresa");
        wholeCompany.Rows.Should().ContainSingle().Which.StaffId.Should().Be(_seller.Id);
        onlyCentro.Total.Should().Be(1_000m);
    }
}
