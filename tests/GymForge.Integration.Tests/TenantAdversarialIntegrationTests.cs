using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Charges;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Members;
using GymForge.Application.UseCases.Reports;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using GymForge.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>
/// Pruebas "de atacante" del aislamiento multi-tenant (ADR-002) contra SQLite real: un usuario de la
/// empresa A intenta leer, cobrar o saltarse límites usando datos de otra sede o de la empresa B.
/// Mundo: empresa A con sedes A1 y A2, empresa B con la sede B1. Tenant ambiente por defecto = A1.
/// </summary>
public class TenantAdversarialIntegrationTests : IAsyncLifetime
{
    private static readonly DateOnly AsOf = new(2026, 6, 1);

    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;

    private Company _companyA = null!, _companyB = null!;
    private Site _siteA1 = null!, _siteA2 = null!, _siteB1 = null!;
    private Member _memberA1 = null!, _memberA1Other = null!, _memberA2 = null!, _memberB1 = null!;
    private Charge _chargeA1 = null!, _chargeA1Other = null!, _chargeA2 = null!, _chargeB1 = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);

        _companyA = Company.Create("Gym A", "30-11111111-1");
        _companyB = Company.Create("Gym B", "30-22222222-2");
        _siteA1 = Site.Create(_companyA.Id, "A Centro", "Calle 1");
        _siteA2 = Site.Create(_companyA.Id, "A Norte", "Calle 2");
        _siteB1 = Site.Create(_companyB.Id, "B Centro", "Calle 3");
        await _db.Companies.AddRangeAsync(_companyA, _companyB);
        await _db.Sites.AddRangeAsync(_siteA1, _siteA2, _siteB1);

        _memberA1 = MemberAt(_companyA, _siteA1, "Ana", "40000001");
        _memberA1Other = MemberAt(_companyA, _siteA1, "Aldo", "40000004");
        _memberA2 = MemberAt(_companyA, _siteA2, "Beto", "40000002");
        _memberB1 = MemberAt(_companyB, _siteB1, "Carla", "40000003");
        await _db.Members.AddRangeAsync(_memberA1, _memberA1Other, _memberA2, _memberB1);

        _chargeA1 = ChargeFor(_memberA1);
        _chargeA1Other = ChargeFor(_memberA1Other);
        _chargeA2 = ChargeFor(_memberA2);
        _chargeB1 = ChargeFor(_memberB1);
        await _db.Charges.AddRangeAsync(_chargeA1, _chargeA1Other, _chargeA2, _chargeB1);

        await _db.SaveChangesAsync();
        _tenant.Set(_companyA.Id, _siteA1.Id);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private static Member MemberAt(Company company, Site site, string firstName, string dni)
    {
        var member = Member.Create(company.Id, site.Id, firstName, "Test", DocumentType.DNI, dni, Gender.Female);
        member.UpdateContact(null, $"+54911{dni}");
        return member;
    }

    private static Charge ChargeFor(Member member) =>
        Charge.Create(member.CompanyId, member.SiteId, member.Id, null,
            ConceptType.MembershipFee, "Cuota", 10_000m, AsOf.AddDays(-7));

    private ProcessPaymentCommandHandler PaymentHandler() => new(
        new ChargeRepository(_db), new PaymentRepository(_db), new CashRegister(new ShiftRepository(_db)),
        new MemberCreditRepository(_db), TestServices.ArrearsRelief(_db, FakeClock.OnDay(AsOf)));

    private static ProcessPaymentCommand PayCommand(Guid companyId, Guid siteId, Guid memberId, params Guid[] chargeIds) =>
        new(companyId, siteId, memberId, Guid.NewGuid(), 10_000m, PaymentMethod.Cash, ShiftId: null,
            ChargeIds: chargeIds);

    private async Task AssertNothingWasPaidAsync()
    {
        _db.ChangeTracker.Clear();
        (await _db.Payments.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await _db.PaymentAllocations.CountAsync()).Should().Be(0);
        (await _db.Charges.IgnoreQueryFilters().SumAsync(c => c.AmountPaid)).Should().Be(0m);
    }

    // ── Pagos: un cargo ajeno no se puede cobrar ─────────────────────────────

    [Fact]
    public async Task Payment_ForAChargeOfAnotherMemberOfTheSameSite_IsRejected()
    {
        var act = () => PaymentHandler().Handle(
            PayCommand(_companyA.Id, _siteA1.Id, _memberA1.Id, _chargeA1Other.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await AssertNothingWasPaidAsync();
    }

    [Fact]
    public async Task Payment_ForAChargeOfAnotherSiteOfTheSameCompany_IsRejectedAsNotFound()
    {
        var act = () => PaymentHandler().Handle(
            PayCommand(_companyA.Id, _siteA1.Id, _memberA2.Id, _chargeA2.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await AssertNothingWasPaidAsync();
    }

    [Fact]
    public async Task Payment_ForAChargeOfAnotherCompany_IsRejectedAsNotFound()
    {
        // El atacante declara el socio y la empresa ajenos para que "coincidan" con el cargo.
        var act = () => PaymentHandler().Handle(
            PayCommand(_companyB.Id, _siteB1.Id, _memberB1.Id, _chargeB1.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await AssertNothingWasPaidAsync();
    }

    [Fact]
    public async Task Payment_ForAChargeOfTheMember_PersistsPaymentWithAllocations_AndOnlyThatTenantSeesIt()
    {
        var dto = await PaymentHandler().Handle(
            PayCommand(_companyA.Id, _siteA1.Id, _memberA1.Id, _chargeA1.Id), CancellationToken.None);

        _db.ChangeTracker.Clear();
        var repo = new PaymentRepository(_db);

        using (_tenant.BeginScope(_companyA.Id, _siteA1.Id))
            (await repo.GetByIdAsync(dto.Id))!.Allocations.Should().ContainSingle(
                "el filtro del pago no debe esconder sus allocations (warning EF benigno)");

        using (_tenant.BeginScope(_companyA.Id, _siteA2.Id))
            (await repo.GetByIdAsync(dto.Id)).Should().BeNull("el pago es de otra sede");

        using (_tenant.BeginScope(_companyB.Id, _siteB1.Id))
            (await repo.GetByIdAsync(dto.Id)).Should().BeNull("el pago es de otra empresa");
    }

    [Fact]
    public async Task Payments_OfConsumidorFinal_AreNotDroppedByTheMemberFilter()
    {
        // Payment.Member es opcional: el Include del socio filtrado no debe descartar pagos sin socio.
        var walkIn = Payment.Create(_companyA.Id, _siteA1.Id, null, Guid.NewGuid(), 5_000m, PaymentMethod.Cash);
        await _db.Payments.AddAsync(walkIn);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await new PaymentRepository(_db).GetByPeriodAsync(
            _companyA.Id, _siteA1.Id, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        result.Select(p => p.Id).Should().Contain(walkIn.Id);
    }

    // ── Lecturas desde otro tenant (lo que vería la API con otro tenant) ─────

    [Fact]
    public async Task MemberReads_AgainstAnotherCompany_ReturnNothing()
    {
        var repo = new MemberRepository(_db);

        (await repo.GetByIdAsync(_memberB1.Id)).Should().BeNull();
        (await repo.FindByDocumentAsync(DocumentType.DNI, "40000003", _companyB.Id)).Should().BeNull();
        (await repo.SearchAsync("Carla", _companyB.Id, _siteB1.Id)).Should().BeEmpty();
        (await repo.GetPagedAsync(_companyB.Id, _siteB1.Id, 1, 50)).Should().BeEmpty();
        (await repo.CountAsync(_companyB.Id, _siteB1.Id)).Should().Be(0);
    }

    [Fact]
    public async Task MemberReads_AgainstAnotherSiteOfTheSameCompany_ReturnNothing()
    {
        var repo = new MemberRepository(_db);

        (await repo.GetByIdAsync(_memberA2.Id)).Should().BeNull();
        (await repo.FindByDocumentAsync(DocumentType.DNI, "40000002", _companyA.Id)).Should().BeNull();
        (await repo.GetPagedAsync(_companyA.Id, _siteA2.Id, 1, 50)).Should().BeEmpty();
    }

    [Fact]
    public async Task CredentialLookup_ByTagOrDni_NeverCrossesCompanyOrSite()
    {
        _memberB1.EnrollTag("TAG-B");
        _memberA2.EnrollTag("TAG-A2");
        await _db.SaveChangesAsync();
        var handler = new FindMemberByCredentialQueryHandler(new MemberRepository(_db));

        async Task<Guid?> Lookup(Guid companyId, string code) =>
            (await handler.Handle(new FindMemberByCredentialQuery(companyId, code), CancellationToken.None))?.Id;

        (await Lookup(_companyA.Id, "40000001")).Should().Be(_memberA1.Id, "es de la sede activa");
        (await Lookup(_companyB.Id, "TAG-B")).Should().BeNull("el tenant ambiente es la empresa A");
        (await Lookup(_companyA.Id, "TAG-B")).Should().BeNull("el tag es de la empresa B");
        (await Lookup(_companyA.Id, "40000003")).Should().BeNull("el DNI es de un socio de la empresa B");
        (await Lookup(_companyA.Id, "TAG-A2")).Should().BeNull("el tag es de otra sede");
        (await Lookup(_companyA.Id, "40000002")).Should().BeNull("el DNI es de un socio de otra sede");
    }

    // ── Edición de socio: solo el propio, de la sede activa ──────────────────

    [Theory]
    [InlineData("B1")]   // otra empresa
    [InlineData("A2")]   // otra sede de la misma empresa
    public async Task UpdateMember_OfAnotherCompanyOrSite_IsRejectedAsNotFound_AndNothingChanges(string target)
    {
        var victim = target == "B1" ? _memberB1 : _memberA2;
        var handler = new UpdateMemberCommandHandler(new MemberRepository(_db));
        var cmd = new UpdateMemberCommand(
            _companyA.Id, victim.Id, "Hackeado", "Test", DocumentType.DNI, "99999999", Gender.Male,
            null, null, null, false, null, null, null, null, null, null, BloodType.Unknown);

        var act = () => handler.Handle(cmd, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _db.ChangeTracker.Clear();
        var stored = await _db.Members.IgnoreQueryFilters().SingleAsync(m => m.Id == victim.Id);
        stored.FirstName.Should().NotBe("Hackeado");
        stored.DocumentNumber.Should().NotBe("99999999");
    }

    // ── Licencia: no se esquiva creando socios en otra sede ──────────────────

    [Fact]
    public async Task License_CountsTheWholeCompany_SoAnotherSiteCannotBeUsedToBypassIt()
    {
        var max = LicenseState.Free.MaxMembers;
        var inOtherSite = max - 4;                       // A ya tiene 3 socios (2 en A1, 1 en A2)
        for (var i = 0; i < inOtherSite; i++)
            await _db.Members.AddAsync(MemberAt(_companyA, _siteA2, $"Extra{i}", $"5{i:D7}"));
        await _db.SaveChangesAsync();
        // Empresa A: 2 en A1 + (1 + inOtherSite) en A2 = max - 1 → entra uno más y el siguiente no.
        var handler = new CreateMemberCommandHandler(
            new MemberRepository(_db), new CurrentLicense(),
            new CompanyCalendar(new SystemClock(), new SiteRepository(_db), NullLogger<CompanyCalendar>.Instance));
        CreateMemberCommand NewMember(string dni) => new(
            _companyA.Id, _siteA1.Id, "Nuevo", "Socio", DocumentType.DNI, dni, Gender.Male, null, null, null);

        await handler.Handle(NewMember("60000001"), CancellationToken.None);   // llega a max

        var act = () => handler.Handle(NewMember("60000002"), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>(
            "la sede A1 sola tiene pocos socios, pero la empresa ya está en el límite");
    }

    // ── Mora multi-sede: el job recorre todas las sedes, nunca otra empresa ──

    [Fact]
    public async Task Dunning_NotifiesMembersOfEverySiteOfTheCompany_ButNeverOfAnotherCompany()
    {
        var sender = Substitute.For<INotificationSender>();
        sender.SendAsync(Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>()).Returns(true);
        var service = new DunningService(
            new ChargeRepository(_db), new MemberRepository(_db), sender, new NotificationLogRepository(_db), _tenant,
            new DunningConfig { Enabled = true });

        var sent = await service.RunAsync(_companyA.Id, AsOf, "Gym A");

        sent.Should().Be(3, "A1 (2 socios) + A2 (1 socio); B no entra");
        await sender.DidNotReceive().SendAsync(
            Arg.Is<NotificationMessage>(m => m.ToPhone == _memberB1.Mobile), Arg.Any<CancellationToken>());
        await sender.Received(1).SendAsync(
            Arg.Is<NotificationMessage>(m => m.ToPhone == _memberA2.Mobile), Arg.Any<CancellationToken>());

        _tenant.CompanyId.Should().Be(_companyA.Id, "el scope por cargo debe cerrarse");
        _tenant.SiteId.Should().Be(_siteA1.Id);
    }

    // ── Gatekeeper: un tag de otra empresa no abre la puerta ─────────────────

    [Fact]
    public async Task Gatekeeper_TagOfAnotherCompany_IsUnknown_AndTouchesNothingOfThatCompany()
    {
        var plan = MembershipType.Create(_companyB.Id, "Pack B", MembershipBasis.VisitPack, 10_000m);
        _memberB1.EnrollTag("TAG-B");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var membership = Membership.Create(
            _companyB.Id, _siteB1.Id, _memberB1.Id, plan.Id, today, today.AddMonths(1), visitsRemaining: 10);
        membership.Activate();
        await _db.MembershipTypes.AddAsync(plan);
        await _db.Memberships.AddAsync(membership);
        await _db.SaveChangesAsync();

        var clock = new SystemClock();
        var gatekeeper = new ValidateSwipeUseCase(
            new MemberRepository(_db), new MembershipRepository(_db), new ChargeRepository(_db),
            new AccessLogRepository(_db), Substitute.For<IEventBus>(), clock,
            TestServices.Calendar(_db, clock), _tenant, new GatekeeperConfig());

        var result = await gatekeeper.ValidateSwipeAsync(
            new ValidateSwipeRequest("TAG-B", AccessMethod.RfidCard, 1, _siteA1.Id, _companyA.Id));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.TagUnknown);

        _db.ChangeTracker.Clear();
        (await _db.Memberships.IgnoreQueryFilters().SingleAsync()).VisitsRemaining.Should().Be(10);
        var log = await _db.AccessLogs.IgnoreQueryFilters().SingleAsync();
        log.CompanyId.Should().Be(_companyA.Id);
        log.MemberId.Should().BeNull();
    }

    // ── Reportes de dueño: cruzan las sedes de UNA empresa, nunca otra ───────

    private async Task<(Payment A1, Payment A2, Payment B1)> SeedPaymentsAsync()
    {
        var a1 = Payment.Create(_companyA.Id, _siteA1.Id, null, Guid.NewGuid(), 10_000m, PaymentMethod.Cash);
        var a2 = Payment.Create(_companyA.Id, _siteA2.Id, null, Guid.NewGuid(), 5_000m, PaymentMethod.BankTransfer);
        var b1 = Payment.Create(_companyB.Id, _siteB1.Id, null, Guid.NewGuid(), 99_000m, PaymentMethod.Cash);
        await _db.Payments.AddRangeAsync(a1, a2, b1);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (a1, a2, b1);
    }

    private GetRevenueBreakdownQueryHandler BreakdownHandler() =>
        new(new PaymentRepository(_db), new SiteRepository(_db));

    private static GetRevenueBreakdownQuery BreakdownQuery(Guid companyId, Guid? siteId) =>
        new(companyId, siteId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

    [Fact]
    public async Task RevenueBreakdown_CompanyWide_CrossesTheSitesOfTheCompany_ButNeverAnotherCompany()
    {
        var (a1, a2, _) = await SeedPaymentsAsync();

        var result = await new PaymentRepository(_db).GetForBreakdownAsync(
            _companyA.Id, null, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        result.Select(p => p.Id).Should().BeEquivalentTo([a1.Id, a2.Id], "B1 es de otra empresa");
    }

    [Fact]
    public async Task RevenueBreakdown_WithAForeignSite_ReturnsNothing()
    {
        await SeedPaymentsAsync();

        var breakdown = await BreakdownHandler().Handle(BreakdownQuery(_companyA.Id, _siteB1.Id), CancellationToken.None);

        breakdown.Total.Should().Be(0m, "la sede B1 no es de la empresa A: el CompanyId explícito la deja afuera");
        breakdown.ByMethod.Should().BeEmpty();
    }

    [Fact]
    public async Task RevenueBreakdown_Handler_ReconcilesTheThreeCutsAcrossSites_WithoutAnotherCompany()
    {
        await SeedPaymentsAsync();

        var breakdown = await BreakdownHandler().Handle(BreakdownQuery(_companyA.Id, null), CancellationToken.None);

        breakdown.Total.Should().Be(15_000m);
        breakdown.ByMethod.Sum(r => r.Amount).Should().Be(15_000m);
        breakdown.ByConcept.Sum(r => r.Amount).Should().Be(15_000m);
        breakdown.BySite.Select(r => (r.Label, r.Amount)).Should().Equal(("A Centro", 10_000m), ("A Norte", 5_000m));
    }

    [Fact]
    public async Task RevenueBreakdown_WithOneSite_DoesNotSeeTheOtherSiteOfTheCompany()
    {
        await SeedPaymentsAsync();

        var breakdown = await BreakdownHandler().Handle(BreakdownQuery(_companyA.Id, _siteA2.Id), CancellationToken.None);

        breakdown.Total.Should().Be(5_000m);
        breakdown.BySite.Should().ContainSingle(r => r.Label == "A Norte");
    }

    private static readonly DateOnly FlowDay = DateOnly.FromDateTime(DateTime.UtcNow);

    private GetMemberFlowQueryHandler FlowHandler() =>
        new(new MemberRepository(_db), new MembershipRepository(_db), new SiteRepository(_db));

    private static GetMemberFlowQuery FlowQuery(Guid companyId, Guid? siteId) =>
        new(companyId, siteId, FlowDay.AddDays(-1), FlowDay.AddDays(1));

    /// <summary>Activa a los cuatro socios del mundo y cancela una membresía en A2 y otra en B1 (hoy).</summary>
    private async Task SeedFlowAsync()
    {
        foreach (var member in new[] { _memberA1, _memberA1Other, _memberA2, _memberB1 })
            member.Activate(FlowDay);

        foreach (var (company, site, member) in new[]
                 {
                     (_companyA, _siteA2, _memberA2),
                     (_companyB, _siteB1, _memberB1),
                 })
        {
            var plan = MembershipType.Create(company.Id, $"Plan {company.LegalName}", MembershipBasis.Renewal, 10_000m);
            var membership = Membership.Create(company.Id, site.Id, member.Id, plan.Id, FlowDay, null);
            membership.Cancel("Baja de prueba");
            await _db.MembershipTypes.AddAsync(plan);
            await _db.Memberships.AddAsync(membership);
        }

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task MemberFlow_CompanyWide_CrossesTheSitesOfTheCompany_ButNeverAnotherCompany()
    {
        await SeedFlowAsync();

        var flow = await FlowHandler().Handle(FlowQuery(_companyA.Id, null), CancellationToken.None);

        flow.ActiveNow.Should().Be(3, "A1 (2 socios) + A2; el socio de B1 no cuenta");
        flow.Joins.Select(j => j.Document).Should().BeEquivalentTo(["40000001", "40000004", "40000002"]);
        flow.Leaves.Should().ContainSingle(l => l.Plan == "Plan Gym A");
        flow.Net.Should().Be(2);
    }

    [Fact]
    public async Task MemberFlow_WithOneSite_DoesNotSeeTheOtherSiteOfTheCompany()
    {
        await SeedFlowAsync();

        var flow = await FlowHandler().Handle(FlowQuery(_companyA.Id, _siteA1.Id), CancellationToken.None);

        flow.ActiveNow.Should().Be(2);
        flow.Joins.Should().HaveCount(2);
        flow.Leaves.Should().BeEmpty("la baja de prueba es de la sede A2");
    }

    [Fact]
    public async Task MemberFlow_WithAForeignSite_ReturnsNothing()
    {
        await SeedFlowAsync();

        var flow = await FlowHandler().Handle(FlowQuery(_companyA.Id, _siteB1.Id), CancellationToken.None);

        flow.ActiveNow.Should().Be(0);
        flow.Joins.Should().BeEmpty();
        flow.Leaves.Should().BeEmpty();
    }

    // ── Migración: el cambio de schema no pierde el historial de accesos ─────

    [Fact]
    public async Task Migration_MakingMemberIdNullable_KeepsExistingAccessLogsAndIndexes()
    {
        var options = new DbContextOptionsBuilder<GymForgeDbContext>().UseSqlite("Data Source=:memory:").Options;
        await using var db = new GymForgeDbContext(options, new AmbientTenantProvider());
        await db.Database.OpenConnectionAsync();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20260616121253_PaymentMemberNullableAndSaleLink");

        var memberId = Guid.NewGuid();
        var logId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO AccessLogs (Id, MemberId, DoorId, SiteId, CompanyId, SwipedAt, Method, TagSerial, " +
            "AccessGranted, Direction, CreatedAt, UpdatedAt) VALUES (" +
            $"'{logId}', '{memberId}', 1, '{Guid.NewGuid()}', '{Guid.NewGuid()}', '2026-06-01 10:00:00', " +
            "'RfidCard', 'TAG-OLD', 1, 'In', '2026-06-01 10:00:00', '2026-06-01 10:00:00');");
        var indexesBefore = await IndexNamesAsync(db, "AccessLogs");

        await migrator.MigrateAsync();

        var indexesAfter = await IndexNamesAsync(db, "AccessLogs");
        indexesAfter.Should().BeEquivalentTo(indexesBefore, "la reconstrucción de la tabla debe conservar los índices");

        db.ChangeTracker.Clear();
        var migrated = await db.AccessLogs.IgnoreQueryFilters().SingleAsync();
        migrated.Id.Should().Be(logId);
        migrated.MemberId.Should().Be(memberId);
        migrated.TagSerial.Should().Be("TAG-OLD");
        migrated.AccessGranted.Should().BeTrue();
    }

    private static async Task<List<string>> IndexNamesAsync(GymForgeDbContext db, string table)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = '{table}' " +
                          "AND name NOT LIKE 'sqlite_autoindex%' ORDER BY name;";
        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }
}
