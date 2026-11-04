using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>AuditLog real vía SaveChangesInterceptor (ADR-003 punto 2), con EF + SQLite reales.</summary>
public class AuditInterceptorIntegrationTests : IAsyncLifetime
{
    private readonly AmbientCurrentUser _user = new();
    private readonly FakeClock _clock = FakeClock.OnDay(new DateOnly(2026, 5, 10));
    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private Company _company = null!;
    private Site _site = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.CreateAudited(out _tenant, _user, _clock);
        _company = Company.Create("Gym Test", "30-12345678-9");
        _site = Site.Create(_company.Id, "Central", "Av. Test 123");
        await _db.Companies.AddAsync(_company);
        await _db.Sites.AddAsync(_site);
        await _db.SaveChangesAsync();
        _tenant.Set(_company.Id, _site.Id);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private Member NewMember(string dni = "30111222") =>
        Member.Create(_company.Id, _site.Id, "Ana", "García", DocumentType.DNI, dni, Gender.Female);

    private async Task<Member> SavedMemberAsync()
    {
        var member = NewMember();
        await _db.Members.AddAsync(member);
        await _db.SaveChangesAsync();
        return member;
    }

    private Task<List<AuditLog>> AuditRowsAsync(string entityType, string? action = null) =>
        _db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityType == entityType && (action == null || a.Action == action))
            .ToListAsync();

    // ── Insert ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Insert_WritesAuditRowWithoutDiff()
    {
        var member = await SavedMemberAsync();

        var row = (await AuditRowsAsync(nameof(Member))).Should().ContainSingle().Subject;
        row.Action.Should().Be("Insert");
        row.EntityId.Should().Be(member.Id);
        row.CompanyId.Should().Be(_company.Id);
        row.DiffJson.Should().BeNull("la creación es autoevidente");
    }

    [Fact]
    public async Task Insert_AuditRowTravelsInTheSameSaveAsTheEntity()
    {
        await SavedMemberAsync();

        _db.ChangeTracker.Clear();
        (await _db.Members.CountAsync()).Should().Be(1);
        (await AuditRowsAsync(nameof(Member))).Should().HaveCount(1);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_WritesOnlyTheChangedProperties()
    {
        var member = await SavedMemberAsync();

        member.Suspend();
        await _db.SaveChangesAsync();

        var row = (await AuditRowsAsync(nameof(Member), "Update")).Should().ContainSingle().Subject;
        row.EntityId.Should().Be(member.Id);
        row.DiffJson.Should().Be("""{"Status":{"old":"Prospect","new":"Suspended"}}""");
    }

    [Fact]
    public async Task Update_WithSeveralChanges_ListsEachOne()
    {
        var member = await SavedMemberAsync();

        member.UpdateContact("ana@test.com", "+5491155550000");
        await _db.SaveChangesAsync();

        var diff = (await AuditRowsAsync(nameof(Member), "Update")).Single().DiffJson;
        diff.Should().Contain("\"Email\":{\"old\":null,\"new\":\"[redacted]\"}");
        diff.Should().Contain("\"Mobile\":{\"old\":null,\"new\":\"[redacted]\"}");
        diff.Should().NotContain("UpdatedAt").And.NotContain("CreatedAt");
    }

    [Fact]
    public async Task Update_OnlyTimestamp_WritesNoRow()
    {
        var member = await SavedMemberAsync();

        _db.Members.Update(member);   // marca todo como modificado sin cambiar ningún valor
        await _db.SaveChangesAsync();

        (await AuditRowsAsync(nameof(Member), "Update")).Should().BeEmpty();
    }

    [Fact]
    public async Task Update_OfAnotherEntityType_UsesItsOwnTypeName()
    {
        var charge = Charge.Create(_company.Id, _site.Id, (await SavedMemberAsync()).Id, null,
            ConceptType.MembershipFee, "Cuota", 10_000m, new DateOnly(2026, 5, 1));
        await _db.Charges.AddAsync(charge);
        await _db.SaveChangesAsync();

        charge.MarkOverdue();
        await _db.SaveChangesAsync();

        var row = (await AuditRowsAsync(nameof(Charge), "Update")).Should().ContainSingle().Subject;
        row.DiffJson.Should().Be("""{"Status":{"old":"Pending","new":"Overdue"}}""");
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_SnapshotsTheOriginalValues()
    {
        var member = await SavedMemberAsync();

        _db.Members.Remove(member);
        await _db.SaveChangesAsync();

        var row = (await AuditRowsAsync(nameof(Member), "Delete")).Should().ContainSingle().Subject;
        row.EntityId.Should().Be(member.Id);
        row.DiffJson.Should().Contain("\"FirstName\":\"Ana\"").And.Contain("\"DocumentNumber\":\"30111222\"");
    }

    // ── Lo que NO se audita ───────────────────────────────────────────────────

    [Fact]
    public async Task AuditLog_AccessLog_AndTheCreditLedger_AreNeverAudited()
    {
        var member = await SavedMemberAsync();
        await _db.AccessLogs.AddAsync(AccessLog.Granted(
            _company.Id, _site.Id, member.Id, null, 1, AccessMethod.RfidCard, AccessDirection.In));
        await _db.MemberCreditMovements.AddAsync(
            MemberCreditMovement.CreditFromOverpayment(_company.Id, _site.Id, member.Id, 500m, Guid.NewGuid()));
        await _db.SaveChangesAsync();

        // Un segundo guardado con filas de auditoría ya persistidas tampoco las re-audita.
        member.Suspend();
        await _db.SaveChangesAsync();

        var audited = await _db.AuditLogs.IgnoreQueryFilters().Select(a => a.EntityType).Distinct().ToListAsync();
        audited.Should().NotContain(new[] { nameof(AuditLog), nameof(AccessLog), nameof(MemberCreditMovement) });
        audited.Should().Contain(nameof(Member));
    }

    [Fact]
    public void OnlyTheAgreedEntitiesAreMarkedAuditable()
    {
        var auditable = typeof(IAuditable).Assembly.GetTypes()
            .Where(t => typeof(IAuditable).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .Select(t => t.Name);

        auditable.Should().BeEquivalentTo(
            "Member", "Membership", "Charge", "Payment", "MembershipType", "Product",
            "Sale", "Shift", "CashMovement", "Staff", "Site", "Booking", "StockBySite");
    }

    [Fact]
    public async Task AuditRows_AreNeverModifiedOrDeletedByLaterSaves()
    {
        var member = await SavedMemberAsync();
        var before = await _db.AuditLogs.IgnoreQueryFilters().AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        var existingIds = before.Select(b => b.Id).ToList();

        member.Suspend();
        _clock.Now = _clock.Now.AddHours(1);
        await _db.SaveChangesAsync();

        var after = await _db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => existingIds.Contains(a.Id)).OrderBy(a => a.Id).ToListAsync();
        after.Should().BeEquivalentTo(before, "las filas previas de AuditLog quedan intactas");
    }

    // ── Quién, cuándo y de qué empresa ────────────────────────────────────────

    [Fact]
    public async Task UserId_ComesFromTheCurrentUser()
    {
        var staffId = Guid.NewGuid();
        _user.Set(staffId);

        await SavedMemberAsync();

        (await AuditRowsAsync(nameof(Member))).Single().UserId.Should().Be(staffId);
    }

    [Fact]
    public async Task UserId_IsNullWhenNobodyIsSignedIn()
    {
        _user.Set(null);

        await SavedMemberAsync();

        (await AuditRowsAsync(nameof(Member))).Single().UserId.Should().BeNull();
    }

    [Fact]
    public async Task Timestamp_ComesFromTheClock()
    {
        await SavedMemberAsync();

        (await AuditRowsAsync(nameof(Member))).Single().Timestamp.Should().Be(_clock.Now);
    }

    [Fact]
    public async Task CompanyId_ComesFromTheEntity_NotFromTheAmbientTenant()
    {
        var member = NewMember();
        using (_tenant.BeginScope(Guid.NewGuid(), Guid.NewGuid()))
        {
            await _db.Members.AddAsync(member);
            await _db.SaveChangesAsync();
        }

        (await AuditRowsAsync(nameof(Member))).Single().CompanyId.Should().Be(_company.Id);
    }

    [Fact]
    public async Task EntityWithoutCompanyId_FallsBackToTheAmbientTenant()
    {
        var shift = Shift.Open(_company.Id, _site.Id, Guid.NewGuid(), 1_000m);
        await _db.Shifts.AddAsync(shift);
        await _db.SaveChangesAsync();

        await _db.CashMovements.AddAsync(CashMovement.Create(
            shift.Id, CashMovementType.Income, CashMovementCategory.Membership, 500m));
        await _db.SaveChangesAsync();

        (await AuditRowsAsync(nameof(CashMovement))).Single().CompanyId.Should().Be(_company.Id);
    }

    [Fact]
    public async Task AuditRows_AreHiddenFromOtherTenants()
    {
        await SavedMemberAsync();

        using (_tenant.BeginScope(Guid.NewGuid(), Guid.NewGuid()))
            (await _db.AuditLogs.CountAsync()).Should().Be(0);
        (await _db.AuditLogs.CountAsync()).Should().BeGreaterThan(0);
    }

    // ── Datos sensibles ───────────────────────────────────────────────────────

    [Fact]
    public async Task Update_NeverStoresThePinHash()
    {
        var staff = Staff.Create(_company.Id, "Luis", "Paz", StaffRole.Admin, "hash-viejo");
        await _db.Staff.AddAsync(staff);
        await _db.SaveChangesAsync();

        staff.ChangePin("hash-nuevo");
        await _db.SaveChangesAsync();

        var diff = (await AuditRowsAsync(nameof(Staff), "Update")).Single().DiffJson;
        diff.Should().Contain("PinCodeHash").And.NotContain("hash-viejo").And.NotContain("hash-nuevo");
    }

    [Fact]
    public async Task Update_BinaryColumns_RecordTheSizeNotTheBytes()
    {
        var member = await SavedMemberAsync();

        member.SetFingerprintTemplate([1, 2, 3, 4, 5]);
        await _db.SaveChangesAsync();

        var diff = (await AuditRowsAsync(nameof(Member), "Update")).Single().DiffJson;
        diff.Should().Contain("FingerprintTemplate").And.Contain("5 bytes");
        diff.Should().NotContain("AQIDBAU=", "regla 4: nunca base64 de la huella");
    }

    // ── Transacción y rutas ───────────────────────────────────────────────────

    [Fact]
    public async Task FailedSave_RollsBackTheAuditRowsToo()
    {
        var member = NewMember();
        var orphanCharge = Charge.Create(_company.Id, _site.Id, Guid.NewGuid(), null,
            ConceptType.MembershipFee, "Cuota huérfana", 1_000m, new DateOnly(2026, 5, 1));
        await _db.Members.AddAsync(member);
        await _db.Charges.AddAsync(orphanCharge);   // FK a un socio inexistente → el guardado falla

        var act = () => _db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        _db.ChangeTracker.Clear();
        (await _db.Members.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await AuditRowsAsync(nameof(Member))).Should().BeEmpty();
        (await AuditRowsAsync(nameof(Charge))).Should().BeEmpty();
    }

    [Fact]
    public async Task FailedSave_ThenRetry_WritesExactlyOneAuditRowPerEntity()
    {
        // El DbContext del escritorio vive toda la app: tras una falla, las entidades siguen pendientes.
        var member = NewMember();
        var orphanCharge = Charge.Create(_company.Id, _site.Id, Guid.NewGuid(), null,
            ConceptType.MembershipFee, "Cuota huérfana", 1_000m, new DateOnly(2026, 5, 1));
        await _db.Members.AddAsync(member);
        await _db.Charges.AddAsync(orphanCharge);
        await FluentActions.Awaiting(() => _db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();

        _db.Entry(orphanCharge).State = EntityState.Detached;   // el usuario corrige y reintenta solo el socio
        await _db.SaveChangesAsync();

        (await AuditRowsAsync(nameof(Member))).Should().ContainSingle("la fila del intento fallido se descartó");
        (await AuditRowsAsync(nameof(Charge))).Should().BeEmpty("el cargo huérfano nunca se guardó");
    }

    [Fact]
    public async Task CanceledSave_LeavesNoQueuedAuditRowsBehind()
    {
        await _db.Members.AddAsync(NewMember());
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await FluentActions.Awaiting(() => _db.SaveChangesAsync(canceled.Token)).Should().ThrowAsync<OperationCanceledException>();

        _db.ChangeTracker.Entries<AuditLog>().Should().NotContain(e => e.State == EntityState.Added);
    }

    [Fact]
    public async Task SynchronousSaveChanges_AlsoAudits()
    {
        await _db.Members.AddAsync(NewMember());

        _db.SaveChanges();

        (await AuditRowsAsync(nameof(Member))).Should().ContainSingle();
    }

    [Fact]
    public async Task SaveWithoutAuditableChanges_WritesNoRows()
    {
        var before = await _db.AuditLogs.IgnoreQueryFilters().CountAsync();

        await _db.SaveChangesAsync();

        (await _db.AuditLogs.IgnoreQueryFilters().CountAsync()).Should().Be(before);
    }
}
