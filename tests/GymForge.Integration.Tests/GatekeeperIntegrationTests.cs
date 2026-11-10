using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using GymForge.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>
/// El Gatekeeper contra SQLite real (repos + EF): verifica que lo que decide en memoria
/// llegue efectivamente a la base. El tenant ambiente queda SIN fijar a propósito: el único
/// que lo establece es el scope que abre el propio Gatekeeper con la sede de la puerta.
/// </summary>
public class GatekeeperIntegrationTests : IAsyncLifetime
{
    private const int DoorId = 1;

    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private Guid _companyId;
    private Guid _siteId;
    private Guid _otherSiteId;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);

        var company = Company.Create("Test Gym", "30-12345678-9");
        await _db.Companies.AddAsync(company);
        var site = Site.Create(company.Id, "Sede Central", "Av. Test 123");
        var otherSite = Site.Create(company.Id, "Sede Norte", "Av. Test 456");
        await _db.Sites.AddRangeAsync(site, otherSite);
        await _db.SaveChangesAsync();

        _companyId = company.Id;
        _siteId = site.Id;
        _otherSiteId = otherSite.Id;
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private ValidateSwipeUseCase CreateGatekeeper(IClock? clock = null)
    {
        clock ??= new SystemClock();
        return new ValidateSwipeUseCase(
            new MemberRepository(_db),
            new MembershipRepository(_db),
            new ChargeRepository(_db),
            new AccessLogRepository(_db),
            Substitute.For<IEventBus>(),
            clock,
            TestServices.Calendar(_db, clock),
            _tenant,
            new GatekeeperConfig());
    }

    private ValidateSwipeRequest TagSwipe(string tag) =>
        new(tag, AccessMethod.RfidCard, DoorId, _siteId, _companyId);

    private async Task SeedMemberWithVisitPackAsync(
        string tag, int visits, Guid siteId, Action<Member>? configureMember = null)
    {
        var plan = MembershipType.Create(_companyId, "Pack de visitas", MembershipBasis.VisitPack, 10_000m);
        var member = Member.Create(_companyId, siteId, "Ana", "García", DocumentType.DNI, "30123456", Gender.Female);
        member.EnrollTag(tag);
        member.Activate(DateOnly.FromDateTime(DateTime.UtcNow));
        configureMember?.Invoke(member);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var membership = Membership.Create(
            _companyId, siteId, member.Id, plan.Id, today, today.AddMonths(1), visitsRemaining: visits);
        membership.Activate();

        await _db.MembershipTypes.AddAsync(plan);
        await _db.Members.AddAsync(member);
        await _db.Memberships.AddAsync(membership);
        await _db.SaveChangesAsync();
    }

    private async Task SeedMemberWithMembershipEndingAsync(string tag, DateOnly endDate)
    {
        var plan = MembershipType.Create(_companyId, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        var member = Member.Create(_companyId, _siteId, "Ana", "García", DocumentType.DNI, "30123456", Gender.Female);
        member.EnrollTag(tag);
        member.Activate(DateOnly.FromDateTime(DateTime.UtcNow));
        var membership = Membership.Create(
            _companyId, _siteId, member.Id, plan.Id, endDate.AddMonths(-1), endDate);
        membership.Activate();

        await _db.MembershipTypes.AddAsync(plan);
        await _db.Members.AddAsync(member);
        await _db.Memberships.AddAsync(membership);
        await _db.SaveChangesAsync();
    }

    // ── Vigencia contra la fecha local de la empresa (Argentina, UTC-3) ───────

    [Fact]
    public async Task LastDayOfValidity_At2200Argentina_IsGranted_AndTheLogRecordsIt()
    {
        var lastDay = new DateOnly(2026, 7, 21);
        await SeedMemberWithMembershipEndingAsync("TAG-LAST", lastDay);

        // 22:00 del 21/07 en Argentina = 01:00 UTC del 22/07: con la fecha UTC se la daba por vencida.
        var result = await CreateGatekeeper(FakeClock.At2200Argentina(lastDay)).ValidateSwipeAsync(TagSwipe("TAG-LAST"));

        result.Granted.Should().BeTrue();
        _db.ChangeTracker.Clear();
        (await _db.AccessLogs.IgnoreQueryFilters().SingleAsync()).AccessGranted.Should().BeTrue();
    }

    [Fact]
    public async Task TheDayAfterTheLastDay_IsDeniedAsExpired_AndTheLogRecordsIt()
    {
        var lastDay = new DateOnly(2026, 7, 21);
        await SeedMemberWithMembershipEndingAsync("TAG-LAST", lastDay);

        var result = await CreateGatekeeper(FakeClock.OnDay(lastDay.AddDays(1))).ValidateSwipeAsync(TagSwipe("TAG-LAST"));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.Expired);
        _db.ChangeTracker.Clear();
        var log = await _db.AccessLogs.IgnoreQueryFilters().SingleAsync();
        log.AccessGranted.Should().BeFalse();
        log.DenialReason.Should().Be(AccessDenialReason.Expired);
    }

    [Fact]
    public async Task VisitPack_GrantedSwipe_PersistsTheDecrementedVisit()
    {
        await SeedMemberWithVisitPackAsync("TAG-PACK", visits: 10, _siteId);

        var result = await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-PACK"));

        result.Granted.Should().BeTrue();

        // Soltar el ChangeTracker fuerza a releer de la base: prueba que persistió, no solo que mutó en memoria.
        _db.ChangeTracker.Clear();
        var persisted = await _db.Memberships.IgnoreQueryFilters().SingleAsync();
        persisted.VisitsRemaining.Should().Be(9);
        (await _db.AccessLogs.IgnoreQueryFilters().CountAsync(a => a.AccessGranted)).Should().Be(1);
    }

    [Fact]
    public async Task SuspendedMember_WithActiveVisitPack_IsDenied_LogsItWithTheMember_AndKeepsTheVisits()
    {
        await SeedMemberWithVisitPackAsync("TAG-SUSP", visits: 10, _siteId, m => m.Suspend());

        var result = await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-SUSP"));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.Suspended);
        _db.ChangeTracker.Clear();
        var member = await _db.Members.IgnoreQueryFilters().SingleAsync();
        var log = await _db.AccessLogs.IgnoreQueryFilters().SingleAsync();
        log.MemberId.Should().Be(member.Id);
        log.AccessGranted.Should().BeFalse();
        log.DenialReason.Should().Be(AccessDenialReason.Suspended);
        (await _db.Memberships.IgnoreQueryFilters().SingleAsync()).VisitsRemaining.Should().Be(10);
    }

    // ── Anti-passback: solo cuenta la última entrada CONCEDIDA ────────────────

    [Fact]
    public async Task AntiPassback_SecondSwipeInsideTheWindow_IsDeniedAsAlreadyInside()
    {
        await SeedMemberWithVisitPackAsync("TAG-PACK", visits: 10, _siteId);
        await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-PACK"));

        var result = await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-PACK"));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.AlreadyInside);
    }

    [Fact]
    public async Task AntiPassback_RepeatedSwipesInsideTheWindow_NeverSlipThrough()
    {
        // La entrada concedida manda: los AlreadyInside que genera quien insiste con la tarjeta
        // quedan logueados como "In" denegados y no pueden "limpiar" el anti-passback.
        await SeedMemberWithVisitPackAsync("TAG-PACK", visits: 10, _siteId);
        await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-PACK"));
        await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-PACK"));

        var third = await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-PACK"));

        third.Granted.Should().BeFalse();
        third.DenialReason.Should().Be(AccessDenialReason.AlreadyInside);
    }

    [Fact]
    public async Task AntiPassback_IgnoresADeniedSwipe_SoThePaidMemberEntersRightAway()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedMemberWithMembershipEndingAsync("TAG-DEBT", today.AddMonths(1));
        var debt = await SeedDebtAsync(15_000m, today);   // > StopOnOweAmount (10_000)
        (await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-DEBT")))
            .DenialReason.Should().Be(AccessDenialReason.Owing);
        await PayAsync(debt);

        var result = await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-DEBT"));

        result.Granted.Should().BeTrue("el swipe denegado por deuda no cuenta como una entrada");
    }

    private async Task<Charge> SeedDebtAsync(decimal amount, DateOnly dueDate)
    {
        var member = await _db.Members.IgnoreQueryFilters().SingleAsync();
        var charge = Charge.Create(_companyId, _siteId, member.Id, null, ConceptType.MembershipFee, "Cuota", amount, dueDate);
        await _db.Charges.AddAsync(charge);
        await _db.SaveChangesAsync();
        return charge;
    }

    private async Task PayAsync(Charge charge)
    {
        charge.ApplyPayment(charge.TotalAmount);
        await _db.SaveChangesAsync();
    }

    // ── Lector de códigos: AccessMethod.Barcode se persiste como texto (sin migración) ─────

    private ValidateSwipeRequest BarcodeSwipe(string code) =>
        new(code, AccessMethod.Barcode, DoorId, _siteId, _companyId);

    [Fact]
    public async Task BarcodeSwipe_WithTheGymTag_IsGranted_AndTheLogStoresTheMethodAsText()
    {
        await SeedMemberWithVisitPackAsync("CARNET-77", visits: 10, _siteId);

        var result = await CreateGatekeeper().ValidateSwipeAsync(BarcodeSwipe("CARNET-77"));

        result.Granted.Should().BeTrue();
        _db.ChangeTracker.Clear();
        (await _db.AccessLogs.IgnoreQueryFilters().SingleAsync()).Method.Should().Be(AccessMethod.Barcode);
        (await _db.Database.SqlQueryRaw<string>("SELECT Method AS Value FROM AccessLogs").SingleAsync())
            .Should().Be("Barcode");
    }

    [Fact]
    public async Task BarcodeSwipe_WithTheDniOfAMemberWhoseTagIsAnother_IsGrantedByTheFallback()
    {
        await SeedMemberWithVisitPackAsync("CARNET-77", visits: 10, _siteId);   // DNI 30123456

        var result = await CreateGatekeeper().ValidateSwipeAsync(BarcodeSwipe("30123456"));

        result.Granted.Should().BeTrue();
        _db.ChangeTracker.Clear();
        (await _db.AccessLogs.IgnoreQueryFilters().SingleAsync()).Method.Should().Be(AccessMethod.Barcode);
    }

    [Fact]
    public async Task BarcodeSwipe_WithTheDniOfAMemberOfAnotherSite_IsUnknownAtThisDoor()
    {
        await SeedMemberWithVisitPackAsync("CARNET-NORTE", visits: 10, _otherSiteId);   // DNI 30123456

        var result = await CreateGatekeeper().ValidateSwipeAsync(BarcodeSwipe("30123456"));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.TagUnknown);
    }

    [Fact]
    public async Task UnknownTag_PersistsADeniedLogWithoutMember()
    {
        var result = await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-NOBODY"));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.TagUnknown);

        _db.ChangeTracker.Clear();
        var log = await _db.AccessLogs.IgnoreQueryFilters().SingleAsync();
        log.MemberId.Should().BeNull();
        log.AccessGranted.Should().BeFalse();
        log.DenialReason.Should().Be(AccessDenialReason.TagUnknown);
        log.TagSerial.Should().Be("TAG-NOBODY");
        log.SiteId.Should().Be(_siteId);
    }

    [Fact]
    public async Task MemberOfAnotherSite_IsUnknownAtThisDoor()
    {
        await SeedMemberWithVisitPackAsync("TAG-NORTE", visits: 10, _otherSiteId);

        var result = await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-NORTE"));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.TagUnknown);

        _db.ChangeTracker.Clear();
        (await _db.Memberships.IgnoreQueryFilters().SingleAsync()).VisitsRemaining.Should().Be(10);
    }

    [Fact]
    public async Task Swipe_RestoresTheAmbientTenantWhenDone()
    {
        await SeedMemberWithVisitPackAsync("TAG-PACK", visits: 10, _siteId);

        await CreateGatekeeper().ValidateSwipeAsync(TagSwipe("TAG-PACK"));

        _tenant.CompanyId.Should().BeEmpty();
        _tenant.SiteId.Should().BeEmpty();
    }
}
