using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// Lecturas de los reportes (ADR-007) contra SQLite real. Mundo: empresa A con las sedes Centro y Norte, y la
/// empresa B con una sede. El tenant ambiente es Centro: las lecturas "de dueño" tienen que abrir Norte igual.
/// </summary>
public class ReportRepositoriesIntegrationTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private Company _companyA = null!, _companyB = null!;
    private Site _centro = null!, _norte = null!, _siteB = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);
        _companyA = Company.Create("Gym A", "30-11111111-1");
        _companyB = Company.Create("Gym B", "30-22222222-2");
        _centro = Site.Create(_companyA.Id, "Centro", "Calle 1");
        _norte = Site.Create(_companyA.Id, "Norte", "Calle 2");
        _siteB = Site.Create(_companyB.Id, "B Centro", "Calle 3");
        await _db.Companies.AddRangeAsync(_companyA, _companyB);
        await _db.Sites.AddRangeAsync(_centro, _norte, _siteB);
        await _db.SaveChangesAsync();
        _tenant.Set(_companyA.Id, _centro.Id);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private static DateTime HourAgo => DateTime.UtcNow.AddHours(-1);
    private static DateTime InAnHour => DateTime.UtcNow.AddHours(1);

    private async Task<Member> AddMemberAsync(Company company, Site site, string dni)
    {
        var member = Member.Create(company.Id, site.Id, "Socio", "Test", DocumentType.DNI, dni, Gender.Female);
        await _db.Members.AddAsync(member);
        await _db.SaveChangesAsync();
        return member;
    }

    private async Task<Payment> AddPaymentAsync(
        Company company, Site site, decimal amount, PaymentMethod method = PaymentMethod.Cash, bool refunded = false)
    {
        var payment = Payment.Create(company.Id, site.Id, memberId: null, Guid.NewGuid(), amount, method);
        if (refunded) payment.Refund();
        await _db.Payments.AddAsync(payment);
        await _db.SaveChangesAsync();
        return payment;
    }

    // ── PaymentRepository.GetForBreakdownAsync ───────────────────────────────

    [Fact]
    public async Task Breakdown_RespetaElPeriodo_YSoloTraePagosCompletados()
    {
        var inRange = await AddPaymentAsync(_companyA, _centro, 10_000m);
        await AddPaymentAsync(_companyA, _centro, 3_000m, refunded: true);
        var repo = new PaymentRepository(_db);

        var inside = await repo.GetForBreakdownAsync(_companyA.Id, _centro.Id, HourAgo, InAnHour);
        var after = await repo.GetForBreakdownAsync(_companyA.Id, _centro.Id, InAnHour, InAnHour.AddHours(1));
        var before = await repo.GetForBreakdownAsync(_companyA.Id, _centro.Id, HourAgo.AddHours(-2), HourAgo);

        inside.Select(p => p.Id).Should().Equal(inRange.Id);
        after.Should().BeEmpty();
        before.Should().BeEmpty();
    }

    [Fact]
    public async Task Breakdown_ConUnaSede_NoTraeLosPagosDeOtraSedeDeLaEmpresa()
    {
        var centro = await AddPaymentAsync(_companyA, _centro, 10_000m);
        await AddPaymentAsync(_companyA, _norte, 5_000m);

        var result = await new PaymentRepository(_db).GetForBreakdownAsync(_companyA.Id, _centro.Id, HourAgo, InAnHour);

        result.Select(p => p.Id).Should().Equal(centro.Id);
    }

    [Fact]
    public async Task Breakdown_ConAlcanceDeEmpresa_CruzaLasSedesAunqueElTenantAmbienteSeaOtra()
    {
        var centro = await AddPaymentAsync(_companyA, _centro, 10_000m);
        var norte = await AddPaymentAsync(_companyA, _norte, 5_000m);

        var result = await new PaymentRepository(_db).GetForBreakdownAsync(_companyA.Id, siteId: null, HourAgo, InAnHour);

        result.Select(p => p.Id).Should().BeEquivalentTo([centro.Id, norte.Id]);
    }

    [Fact]
    public async Task Breakdown_CargaElGrafoHastaElPlan()
    {
        var member = await AddMemberAsync(_companyA, _norte, "40000001");
        var plan = MembershipType.Create(_companyA.Id, "Mensual Full", MembershipBasis.Renewal, 35_000m);
        var membership = Membership.Create(_companyA.Id, _norte.Id, member.Id, plan.Id, new DateOnly(2026, 11, 1), null);
        var fee = Charge.Create(_companyA.Id, _norte.Id, member.Id, membership.Id,
            ConceptType.MembershipFee, "Cuota", 35_000m, new DateOnly(2026, 11, 1));
        var loose = Charge.Create(_companyA.Id, _norte.Id, member.Id, null,
            ConceptType.ProductSale, "Agua", 1_000m, new DateOnly(2026, 11, 1));
        var payment = Payment.Create(_companyA.Id, _norte.Id, member.Id, Guid.NewGuid(), 36_000m, PaymentMethod.Cash);
        payment.Allocations.Add(PaymentAllocation.Create(payment.Id, fee.Id, 35_000m));
        payment.Allocations.Add(PaymentAllocation.Create(payment.Id, loose.Id, 1_000m));
        await _db.MembershipTypes.AddAsync(plan);
        await _db.Memberships.AddAsync(membership);
        await _db.Charges.AddRangeAsync(fee, loose);
        await _db.Payments.AddAsync(payment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await new PaymentRepository(_db).GetForBreakdownAsync(_companyA.Id, null, HourAgo, InAnHour);

        var allocations = result.Single().Allocations;
        allocations.Should().HaveCount(2);
        allocations.Single(a => a.Charge.ConceptType == ConceptType.MembershipFee)
            .Charge.Membership!.MembershipType.Name.Should().Be("Mensual Full");
        allocations.Single(a => a.Charge.ConceptType == ConceptType.ProductSale).Charge.Membership.Should().BeNull();
    }

    // ── Altas y bajas: lecturas de socios y membresías ───────────────────────

    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 3);

    private async Task<Member> AddJoinedMemberAsync(Company company, Site site, string dni, DateOnly? joinDate)
    {
        var member = Member.Create(company.Id, site.Id, "Socio", "Test", DocumentType.DNI, dni, Gender.Female);
        if (joinDate is { } date) member.Activate(date);
        await _db.Members.AddAsync(member);
        await _db.SaveChangesAsync();
        return member;
    }

    private async Task<Membership> AddCancelledMembershipAsync(
        Company company, Site site, string dni, DateTime cancelDate, string reason = "Se mudó")
    {
        var member = await AddJoinedMemberAsync(company, site, dni, From);
        var plan = MembershipType.Create(company.Id, $"Plan {dni}", MembershipBasis.Renewal, 35_000m);
        var membership = Membership.Create(company.Id, site.Id, member.Id, plan.Id, From, null);
        membership.Cancel(reason);
        await _db.MembershipTypes.AddAsync(plan);
        await _db.Memberships.AddAsync(membership);
        await _db.SaveChangesAsync();

        // Cancel() sella la hora actual: para probar los bordes se fija la fecha de baja.
        _db.Entry(membership).Property(m => m.CancelDate).CurrentValue = cancelDate;
        await _db.SaveChangesAsync();
        return membership;
    }

    private static DateTime Utc(int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(2026, month, day, hour, minute, second, DateTimeKind.Utc);

    [Fact]
    public async Task JoinedBetween_IncluyeAmbosExtremosDelRango_YNadaAfuera()
    {
        await AddJoinedMemberAsync(_companyA, _centro, "10000001", From.AddDays(-1));
        var onFrom = await AddJoinedMemberAsync(_companyA, _centro, "10000002", From);
        var middle = await AddJoinedMemberAsync(_companyA, _centro, "10000003", From.AddDays(1));
        var onTo = await AddJoinedMemberAsync(_companyA, _centro, "10000004", To);
        await AddJoinedMemberAsync(_companyA, _centro, "10000005", To.AddDays(1));

        var result = await new MemberRepository(_db).GetJoinedBetweenAsync(_companyA.Id, _centro.Id, From, To);

        result.Select(m => m.Id).Should().BeEquivalentTo([onFrom.Id, middle.Id, onTo.Id]);
    }

    [Fact]
    public async Task JoinedBetween_NoTraeAlQueNuncaSeActivo()
    {
        await AddJoinedMemberAsync(_companyA, _centro, "10000001", joinDate: null);

        var result = await new MemberRepository(_db)
            .GetJoinedBetweenAsync(_companyA.Id, null, DateOnly.MinValue, DateOnly.MaxValue);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task JoinedBetween_ConUnaSedeFiltraEsaSede_ConAlcanceDeEmpresaCruzaLasSedes()
    {
        var centro = await AddJoinedMemberAsync(_companyA, _centro, "10000001", From);
        var norte = await AddJoinedMemberAsync(_companyA, _norte, "10000002", From);
        await AddJoinedMemberAsync(_companyB, _siteB, "10000003", From);
        var repo = new MemberRepository(_db);

        var onlyNorte = await repo.GetJoinedBetweenAsync(_companyA.Id, _norte.Id, From, To);
        var wholeCompany = await repo.GetJoinedBetweenAsync(_companyA.Id, null, From, To);

        onlyNorte.Select(m => m.Id).Should().Equal(norte.Id);
        wholeCompany.Select(m => m.Id).Should().BeEquivalentTo([centro.Id, norte.Id], "el socio de la empresa B no entra");
    }

    [Fact]
    public async Task CountByStatus_ContaLaFotoDeHoyPorEstado_SoloDeLaSedeOLaEmpresa()
    {
        await AddJoinedMemberAsync(_companyA, _centro, "10000001", From);
        await AddJoinedMemberAsync(_companyA, _centro, "10000002", From);
        var frozen = await AddJoinedMemberAsync(_companyA, _centro, "10000003", From);
        frozen.Freeze();
        await AddJoinedMemberAsync(_companyA, _norte, "10000004", From);
        await AddJoinedMemberAsync(_companyB, _siteB, "10000005", From);
        await _db.SaveChangesAsync();
        var repo = new MemberRepository(_db);

        var centro = await repo.CountByStatusAsync(_companyA.Id, _centro.Id);
        var wholeCompany = await repo.CountByStatusAsync(_companyA.Id, null);

        centro.Should().BeEquivalentTo(new Dictionary<MemberStatus, int>
        {
            [MemberStatus.Active] = 2, [MemberStatus.Frozen] = 1,
        });
        wholeCompany.Should().BeEquivalentTo(new Dictionary<MemberStatus, int>
        {
            [MemberStatus.Active] = 3, [MemberStatus.Frozen] = 1,
        });
    }

    [Fact]
    public async Task CancelledBetween_IncluyeElDesdeYExcluyeElHastaExclusivo()
    {
        var onFrom = await AddCancelledMembershipAsync(_companyA, _centro, "20000001", Utc(11, 1));
        var lastMoment = await AddCancelledMembershipAsync(_companyA, _centro, "20000002", Utc(11, 3, 23, 59, 59));
        await AddCancelledMembershipAsync(_companyA, _centro, "20000003", Utc(10, 31, 23, 59, 59));
        await AddCancelledMembershipAsync(_companyA, _centro, "20000004", Utc(11, 4));

        var result = await new MembershipRepository(_db)
            .GetCancelledBetweenAsync(_companyA.Id, _centro.Id, Utc(11, 1), Utc(11, 4));

        result.Select(m => m.Id).Should().BeEquivalentTo([onFrom.Id, lastMoment.Id]);
    }

    [Fact]
    public async Task CancelledBetween_TraeElSocioYElPlanCargados_YNoCuentaLasMembresiasNoCanceladas()
    {
        var cancelled = await AddCancelledMembershipAsync(_companyA, _norte, "20000001", Utc(11, 2, 12), "Costo");
        var alive = await AddJoinedMemberAsync(_companyA, _norte, "20000002", From);
        var plan = MembershipType.Create(_companyA.Id, "Vigente", MembershipBasis.Renewal, 1m);
        await _db.MembershipTypes.AddAsync(plan);
        await _db.Memberships.AddAsync(Membership.Create(_companyA.Id, _norte.Id, alive.Id, plan.Id, From, null));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await new MembershipRepository(_db).GetCancelledBetweenAsync(_companyA.Id, null, Utc(11, 1), Utc(11, 4));

        var only = result.Should().ContainSingle().Subject;
        only.Id.Should().Be(cancelled.Id);
        only.Member.DocumentNumber.Should().Be("20000001");
        only.MembershipType.Name.Should().Be("Plan 20000001");
        only.CancelReason.Should().Be("Costo");
    }

    [Fact]
    public async Task CancelledBetween_ConUnaSedeFiltraEsaSede_ConAlcanceDeEmpresaCruzaLasSedes_YNuncaOtraEmpresa()
    {
        var centro = await AddCancelledMembershipAsync(_companyA, _centro, "20000001", Utc(11, 2, 12));
        var norte = await AddCancelledMembershipAsync(_companyA, _norte, "20000002", Utc(11, 2, 12));
        await AddCancelledMembershipAsync(_companyB, _siteB, "20000003", Utc(11, 2, 12));
        var repo = new MembershipRepository(_db);

        var onlyCentro = await repo.GetCancelledBetweenAsync(_companyA.Id, _centro.Id, Utc(11, 1), Utc(11, 4));
        var wholeCompany = await repo.GetCancelledBetweenAsync(_companyA.Id, null, Utc(11, 1), Utc(11, 4));

        onlyCentro.Select(m => m.Id).Should().Equal(centro.Id);
        wholeCompany.Select(m => m.Id).Should().BeEquivalentTo([centro.Id, norte.Id]);
    }
}
