using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>
/// NEW-31: los socios Prospect con membresía Active (datos legados previos a D14) pasan a Active para que el
/// allowlist del Gatekeeper no los deniegue. UPDATE set-based + EXISTS: se prueba contra SQLite real.
/// </summary>
public class MemberStatusReconciliationTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
    private readonly IDatabaseBackup _backup = Substitute.For<IDatabaseBackup>();
    private int _dni = 30_000_000;
    private int _cuit = 11_111_111;

    public Task InitializeAsync()
    {
        _db = TestDb.Create(out _);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private readonly CapturingLogger _logger = new();

    public MemberStatusReconciliationTests() =>
        _backup.BackupNow(Arg.Any<string>()).Returns(true);

    private Task ReconcileAsync() =>
        MemberStatusReconciliation.ReconcileAsync(_db, _backup, _logger, CancellationToken.None);

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private async Task<(Company Company, Site Site)> SeedTenantAsync(string name)
    {
        var company = Company.Create(name, $"30-{_cuit++:D8}-1");
        var site = Site.Create(company.Id, "Central", "Calle 1");
        await _db.Companies.AddAsync(company);
        await _db.Sites.AddAsync(site);
        await _db.SaveChangesAsync();
        return (company, site);
    }

    /// <summary>Socio con el estado dado y, opcionalmente, una membresía con el estado dado (ambos escritos como en la base real).</summary>
    private async Task<Guid> SeedMemberAsync(
        (Company Company, Site Site) tenant, MemberStatus status, MembershipStatus? membershipStatus)
    {
        var member = Member.Create(
            tenant.Company.Id, tenant.Site.Id, "Ana", "García", DocumentType.DNI, (_dni++).ToString(), Gender.Female);
        await _db.Members.AddAsync(member);

        if (membershipStatus is { } ms)
        {
            var plan = MembershipType.Create(tenant.Company.Id, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var membership = Membership.Create(
                tenant.Company.Id, tenant.Site.Id, member.Id, plan.Id, today, today.AddMonths(1));
            await _db.MembershipTypes.AddAsync(plan);
            await _db.Memberships.AddAsync(membership);
            await _db.SaveChangesAsync();
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Memberships SET Status = {ms.ToString()} WHERE Id = {membership.Id}");
        }

        await _db.SaveChangesAsync();
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Members SET Status = {status.ToString()} WHERE Id = {member.Id}");
        _db.ChangeTracker.Clear();
        return member.Id;
    }

    private async Task<string> StatusOfAsync(Guid memberId) =>
        await _db.Database.SqlQuery<string>($"SELECT Status AS Value FROM Members WHERE Id = {memberId}").SingleAsync();

    private async Task<string> StatusSnapshotAsync() =>
        string.Join(",", await _db.Database
            .SqlQuery<string>($"SELECT Status AS Value FROM Members ORDER BY DocumentNumber").ToListAsync());

    // ── Qué se promueve ───────────────────────────────────────────────────────

    [Fact]
    public async Task ProspectConMembresiaActiva_SePromueveAActive()
    {
        var tenant = await SeedTenantAsync("Gym A");
        var id = await SeedMemberAsync(tenant, MemberStatus.Prospect, MembershipStatus.Active);

        await ReconcileAsync();

        (await StatusOfAsync(id)).Should().Be(nameof(MemberStatus.Active));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(MembershipStatus.Expired)]
    [InlineData(MembershipStatus.Cancelled)]
    [InlineData(MembershipStatus.PendingActivation)]
    public async Task ProspectSinMembresiaActiva_QuedaProspect(MembershipStatus? membershipStatus)
    {
        var tenant = await SeedTenantAsync("Gym A");
        var id = await SeedMemberAsync(tenant, MemberStatus.Prospect, membershipStatus);

        await ReconcileAsync();

        (await StatusOfAsync(id)).Should().Be(nameof(MemberStatus.Prospect));
    }

    [Theory]
    [InlineData(MemberStatus.Active)]
    [InlineData(MemberStatus.Overdue)]
    [InlineData(MemberStatus.Suspended)]
    [InlineData(MemberStatus.Frozen)]
    [InlineData(MemberStatus.Expired)]
    [InlineData(MemberStatus.Cancelled)]
    public async Task OtrosEstados_ConMembresiaActiva_NoSeTocan(MemberStatus status)
    {
        var tenant = await SeedTenantAsync("Gym A");
        var id = await SeedMemberAsync(tenant, status, MembershipStatus.Active);

        await ReconcileAsync();

        (await StatusOfAsync(id)).Should().Be(status.ToString(), "no se pisa una sanción ni una baja explícita");
    }

    // ── Idempotencia, backup y auditoría ──────────────────────────────────────

    [Fact]
    public async Task Idempotente_SegundaCorridaNoCambiaNadaNiHaceOtroBackup()
    {
        var tenant = await SeedTenantAsync("Gym A");
        await SeedMemberAsync(tenant, MemberStatus.Prospect, MembershipStatus.Active);
        await ReconcileAsync();
        var afterFirst = await StatusSnapshotAsync();

        await ReconcileAsync();

        (await StatusSnapshotAsync()).Should().Be(afterFirst);
        _backup.Received(1).BackupNow(Arg.Any<string>());
    }

    [Fact]
    public async Task ConFilasAfectadas_HaceUnBackupPrevioEtiquetado()
    {
        var tenant = await SeedTenantAsync("Gym A");
        await SeedMemberAsync(tenant, MemberStatus.Prospect, MembershipStatus.Active);

        await ReconcileAsync();

        _backup.Received(1).BackupNow("pre-reconciliacion-estado-socio");
    }

    [Fact]
    public async Task SinFilasAfectadas_NoHaceBackup()
    {
        var tenant = await SeedTenantAsync("Gym A");
        await SeedMemberAsync(tenant, MemberStatus.Prospect, membershipStatus: null);
        await SeedMemberAsync(tenant, MemberStatus.Active, MembershipStatus.Active);

        await ReconcileAsync();

        _backup.DidNotReceive().BackupNow(Arg.Any<string>());
    }

    [Fact]
    public async Task LogueaLasFilasRealmenteAfectadas()
    {
        var tenant = await SeedTenantAsync("Gym A");
        await SeedMemberAsync(tenant, MemberStatus.Prospect, MembershipStatus.Active);
        await SeedMemberAsync(tenant, MemberStatus.Prospect, MembershipStatus.Active);
        await SeedMemberAsync(tenant, MemberStatus.Prospect, membershipStatus: null);

        await ReconcileAsync();

        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information)
            .Which.Message.Should().Contain("2 socio(s)");
    }

    [Fact]
    public async Task SiElBackupFalla_AvisaPeroRepara()
    {
        _backup.BackupNow(Arg.Any<string>()).Returns(false);
        var tenant = await SeedTenantAsync("Gym A");
        var id = await SeedMemberAsync(tenant, MemberStatus.Prospect, MembershipStatus.Active);

        await ReconcileAsync();

        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("backup"));
        (await StatusOfAsync(id)).Should().Be(nameof(MemberStatus.Active), "no se deja la recepción sin abrir");
    }

    [Fact]
    public async Task SiElBackupSale_NoHayWarning()
    {
        var tenant = await SeedTenantAsync("Gym A");
        await SeedMemberAsync(tenant, MemberStatus.Prospect, MembershipStatus.Active);

        await ReconcileAsync();

        _logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task NoEscribeAuditLog()
    {
        var tenant = await SeedTenantAsync("Gym A");
        await SeedMemberAsync(tenant, MemberStatus.Prospect, MembershipStatus.Active);
        var before = await _db.AuditLogs.IgnoreQueryFilters().CountAsync();

        await ReconcileAsync();

        (await _db.AuditLogs.IgnoreQueryFilters().CountAsync()).Should().Be(before);
    }

    // ── Multi-tenant ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AislamientoMultiTenant_CadaSocioSePromueveSoloPorSusPropiasMembresias()
    {
        var a = await SeedTenantAsync("Gym A");
        var b = await SeedTenantAsync("Gym B");
        var leadDeA = await SeedMemberAsync(a, MemberStatus.Prospect, membershipStatus: null);
        var legadoDeA = await SeedMemberAsync(a, MemberStatus.Prospect, MembershipStatus.Active);
        var legadoDeB = await SeedMemberAsync(b, MemberStatus.Prospect, MembershipStatus.Active);

        await ReconcileAsync();

        (await StatusOfAsync(leadDeA)).Should().Be(nameof(MemberStatus.Prospect), "las membresías de otros socios o tenants no lo promueven");
        (await StatusOfAsync(legadoDeA)).Should().Be(nameof(MemberStatus.Active));
        (await StatusOfAsync(legadoDeB)).Should().Be(nameof(MemberStatus.Active), "el alcance es toda la base");
    }

    // ── Anclaje del formato de persistencia que usa el SQL ───────────────────

    [Fact]
    public async Task EstadosSePersistenComoTexto_ConElNombreDelEnum()
    {
        var tenant = await SeedTenantAsync("Gym A");
        var member = Member.Create(
            tenant.Company.Id, tenant.Site.Id, "Ana", "García", DocumentType.DNI, "30999999", Gender.Female);
        member.Activate(DateOnly.FromDateTime(DateTime.UtcNow));
        var plan = MembershipType.Create(tenant.Company.Id, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        var membership = Membership.Create(
            tenant.Company.Id, tenant.Site.Id, member.Id, plan.Id, DateOnly.FromDateTime(DateTime.UtcNow), null);
        membership.Activate();
        await _db.AddRangeAsync(member, plan, membership);
        await _db.SaveChangesAsync();

        (await StatusOfAsync(member.Id)).Should().Be(nameof(MemberStatus.Active));
        (await _db.Database.SqlQuery<string>($"SELECT Status AS Value FROM Memberships WHERE Id = {membership.Id}")
            .SingleAsync()).Should().Be(nameof(MembershipStatus.Active));
    }
}
