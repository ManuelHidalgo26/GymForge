using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// El AuditLog es append-only: lo que entra no sale nunca. Estos tests garantizan que ni credenciales ni
/// datos de salud ni la huella cruzan al log, ni en los updates ni en los snapshots de los deletes.
/// </summary>
public class AuditSensitiveDataQaTests : IAsyncLifetime
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

    private async Task<Member> SavedMemberAsync()
    {
        var member = Member.Create(_company.Id, _site.Id, "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        await _db.Members.AddAsync(member);
        await _db.SaveChangesAsync();
        return member;
    }

    // Todavía no hay método de dominio que cargue datos de salud: se fijan por el ChangeTracker, que es
    // exactamente lo que verá el interceptor el día que exista la ficha médica.
    private void SetHealthData(Member member, string conditions, string medications, string allergies)
    {
        var entry = _db.Entry(member);
        entry.Property(nameof(Member.MedicalConditions)).CurrentValue = conditions;
        entry.Property(nameof(Member.Medications)).CurrentValue = medications;
        entry.Property(nameof(Member.Allergies)).CurrentValue = allergies;
    }

    private Task<AuditLog> AuditRowAsync(string entityType, string action) =>
        _db.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.EntityType == entityType && a.Action == action);

    [Fact]
    public async Task Delete_OfStaff_SnapshotRedactsThePinHash()
    {
        var staff = Staff.Create(_company.Id, "Luis", "Pérez", StaffRole.Admin, "PBKDF2$SECRETO$HASH");
        await _db.Staff.AddAsync(staff);
        await _db.SaveChangesAsync();

        _db.Staff.Remove(staff);
        await _db.SaveChangesAsync();

        var row = await AuditRowAsync(nameof(Staff), "Delete");
        row.DiffJson.Should().Contain("\"PinCodeHash\":\"[redacted]\"").And.NotContain("SECRETO");
    }

    [Fact]
    public async Task HealthData_NeverReachesTheAuditLog_NeitherInUpdatesNorInDeleteSnapshots()
    {
        var member = await SavedMemberAsync();
        SetHealthData(member, "Diabetes tipo 2", "Metformina", "Penicilina");
        await _db.SaveChangesAsync();

        _db.Members.Remove(member);
        await _db.SaveChangesAsync();

        var update = await AuditRowAsync(nameof(Member), "Update");
        var delete = await AuditRowAsync(nameof(Member), "Delete");
        foreach (var json in new[] { update.DiffJson, delete.DiffJson })
            json.Should().NotContain("Diabetes").And.NotContain("Metformina").And.NotContain("Penicilina");

        update.DiffJson.Should().Contain("\"MedicalConditions\":{\"old\":null,\"new\":\"[redacted]\"}");
        delete.DiffJson.Should().Contain("\"MedicalConditions\":\"[redacted]\"");
    }

    [Fact]
    public async Task HealthData_WhenItChanges_StillLeavesTraceThatItChanged_WithoutTheValue()
    {
        var member = await SavedMemberAsync();
        SetHealthData(member, "Asma", "Salbutamol", "Ninguna");
        await _db.SaveChangesAsync();

        _clock.Now = _clock.Now.AddMinutes(1);   // las filas se ordenan por Timestamp
        SetHealthData(member, "Asma leve", "Salbutamol", "Ninguna");
        await _db.SaveChangesAsync();

        var rows = await _db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityType == nameof(Member) && a.Action == "Update")
            .OrderBy(a => a.Timestamp).ToListAsync();
        rows.Should().HaveCount(2, "el segundo cambio también deja fila aunque el valor no se muestre");
        rows[1].DiffJson.Should().Be("""{"MedicalConditions":{"old":"[redacted]","new":"[redacted]"}}""");
    }

    [Fact]
    public async Task EditingAMember_LogsWhichPersonalFieldsChanged_ButNeverTheirValues()
    {
        var member = await SavedMemberAsync();

        member.EditIdentity("Anita", "Pérez", DocumentType.DNI, "40999888", Gender.Female);
        member.UpdateContact("anita@secreto.com", "+5491100001111");
        member.CompleteProfile(new DateOnly(1990, 5, 20), MemberSource.WalkIn, null, marketingConsent: true);
        member.UpdateEmergencyContact("Luis Pérez", "+5491122223333", "Hermano");
        member.UpdateMedicalInfo("Hipertensión", "Enalapril", "Latex", BloodType.OPositive);
        await _db.SaveChangesAsync();

        var diff = (await AuditRowAsync(nameof(Member), "Update")).DiffJson!;
        foreach (var leaked in new[]
                 {
                     "Anita", "Pérez", "40999888", "30111222", "García", "secreto", "5491100001111", "1990",
                     "Luis", "5491122223333", "Hermano", "Hipertensión", "Enalapril", "Latex", "OPositive"
                 })
            diff.Should().NotContain(leaked);

        foreach (var field in new[]
                 {
                     "FirstName", "LastName", "DocumentNumber", "Email", "Mobile", "BirthDate", "EmergencyName",
                     "EmergencyPhone", "EmergencyRelation", "MedicalConditions", "Medications", "Allergies", "BloodType"
                 })
            diff.Should().Contain($"\"{field}\":{{\"old\":", "queda registrado qué campo cambió");
        diff.Should().Contain("\"MarketingConsent\":{\"old\":false,\"new\":true}", "el consentimiento no es dato personal sensible");
    }

    [Fact]
    public async Task Delete_OfMember_SnapshotKeepsEmptyHealthFieldsEmpty_AndTheFingerprintAsSizeOnly()
    {
        var member = await SavedMemberAsync();
        member.SetFingerprintTemplate([1, 2, 3, 4, 5]);
        await _db.SaveChangesAsync();

        _db.Members.Remove(member);
        await _db.SaveChangesAsync();

        var snapshot = (await AuditRowAsync(nameof(Member), "Delete")).DiffJson;
        snapshot.Should().Contain("\"FingerprintTemplate\":\"<5 bytes>\"").And.NotContain("AQIDBAU=");
        snapshot.Should().Contain("\"MedicalConditions\":null", "un campo vacío no se disfraza de dato redactado");
    }

    [Fact]
    public async Task Delete_OfMember_SnapshotRedactsEveryPersonalField()
    {
        var member = await SavedMemberAsync();
        member.UpdateContact("ana@secreto.com", "+5491100001111");
        var entry = _db.Entry(member);
        entry.Property(nameof(Member.Observations)).CurrentValue = "Le duele la espalda";
        entry.Property(nameof(Member.CustomFieldsJson)).CurrentValue = """{"talle":"remera-XL-secreta"}""";
        entry.Property(nameof(Member.SignatureUrl)).CurrentValue = "firmas/ana-secreta.png";
        entry.Property(nameof(Member.TagSerial)).CurrentValue = "30111222";
        entry.Property(nameof(Member.PhotoUrl)).CurrentValue = "fotos/ana-garcia-foto.jpg";
        await _db.SaveChangesAsync();

        _db.Members.Remove(member);
        await _db.SaveChangesAsync();

        var snapshot = (await AuditRowAsync(nameof(Member), "Delete")).DiffJson!;
        foreach (var leaked in new[]
                 {
                     "Ana", "García", "30111222", "secreto", "5491100001111", "espalda", "remera-XL", "ana-secreta",
                     "ana-garcia-foto"
                 })
            snapshot.Should().NotContain(leaked);

        foreach (var field in new[]
                 {
                     "FirstName", "LastName", "DocumentNumber", "Email", "Mobile", "Observations",
                     "CustomFieldsJson", "SignatureUrl", "TagSerial", "PhotoUrl"
                 })
            snapshot.Should().Contain($"\"{field}\":\"[redacted]\"");
    }

    // Blinda contra una generalización futura de la redacción por nombre de campo: el TagSerial del Member es
    // dato personal (se redacta), pero el de AccessLog es el registro de acceso y tiene que quedar intacto.
    [Fact]
    public async Task AccessLog_TagSerial_IsNotRedacted_AndTheAccessLogIsNotAudited()
    {
        var member = await SavedMemberAsync();
        var swipe = AccessLog.Granted(
            _company.Id, _site.Id, member.Id, null, 1, AccessMethod.RfidCard, AccessDirection.In, "TAG-0001");
        await _db.AccessLogs.AddAsync(swipe);
        await _db.SaveChangesAsync();

        var stored = await _db.AccessLogs.IgnoreQueryFilters().SingleAsync(a => a.Id == swipe.Id);
        stored.TagSerial.Should().Be("TAG-0001");
        (await _db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EntityType == nameof(AccessLog)))
            .Should().BeFalse("el AccessLog es su propio registro y no pasa por el AuditLog");
    }

    [Fact]
    public async Task FreezeReason_NeverReachesTheAuditLog_NeitherOnFreezeNorOnUnfreeze()
    {
        var member = await SavedMemberAsync();
        var plan = MembershipType.Create(_company.Id, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        await _db.MembershipTypes.AddAsync(plan);
        var start = new DateOnly(2026, 5, 1);
        var membership = Membership.Create(
            _company.Id, _site.Id, member.Id, plan.Id, start, start.AddDays(30));
        membership.Activate();
        await _db.Memberships.AddAsync(membership);
        await _db.SaveChangesAsync();

        membership.Freeze(new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 20), "lesión de rodilla");
        await _db.SaveChangesAsync();
        _clock.Now = _clock.Now.AddMinutes(1);
        membership.Unfreeze(new DateOnly(2026, 5, 15));
        await _db.SaveChangesAsync();

        var updates = await _db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityType == nameof(Membership) && a.Action == "Update")
            .OrderBy(a => a.Timestamp).ToListAsync();
        updates.Should().HaveCount(2);
        updates.Should().OnlyContain(a => !a.DiffJson!.Contains("rodilla"));
        updates[0].DiffJson.Should().Contain("\"FreezeReason\":{\"old\":null,\"new\":\"[redacted]\"}");
        updates[1].DiffJson.Should().Contain("\"FreezeReason\":{\"old\":\"[redacted]\",\"new\":null}");
    }
}
