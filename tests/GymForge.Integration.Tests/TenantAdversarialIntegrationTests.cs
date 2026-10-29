using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Charges;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using GymForge.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
        var handler = new CreateMemberCommandHandler(new MemberRepository(_db), new CurrentLicense());
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
            TestServices.Calendar(_db, clock), _tenant, new GatekeeperConfig { Timezone = "UTC" });

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
