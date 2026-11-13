using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Licensing;
using GymForge.Domain.Entities;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>
/// ADR-016: la activación de licencia deja una fila de AuditLog explícita (append-only, tenant-scoped, sin PII),
/// con EF + SQLite reales y el interceptor enganchado.
/// </summary>
public class LicenseAuditIntegrationTests : IAsyncLifetime
{
    private const string GymCuit = "30-71234567-1";
    private const string RawKey = "GYMF.payload-secreto.firma";

    private readonly AmbientCurrentUser _user = new();
    private readonly FakeClock _clock = FakeClock.OnDay(new DateOnly(2026, 6, 11));
    private readonly ILicenseService _licenses = Substitute.For<ILicenseService>();
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();
    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private Company _company = null!;
    private Company _other = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.CreateAudited(out _tenant, _user, _clock);
        _company = Company.Create("Gym A", GymCuit);
        _other = Company.Create("Gym B", "30-69876543-3");
        await _db.Companies.AddRangeAsync(_company, _other);
        await _db.SaveChangesAsync();
        _tenant.Set(_company.Id, Guid.NewGuid());
        _calendar.TodayAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new DateOnly(2026, 6, 11));
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private ActivateLicenseCommandHandler Sut() =>
        new(new SiteRepository(_db), _licenses, new CurrentLicense(), _calendar, _user, _clock,
            NullLogger<ActivateLicenseCommandHandler>.Instance);

    private static LicenseState KeyFor(string cuit) =>
        new(LicenseStatus.Active, "Pro", 3, 1000, new DateOnly(2027, 6, 11), "Gym A", cuit);

    private Task<List<AuditLog>> LicenseRowsAsync() =>
        _db.AuditLogs.IgnoreQueryFilters().Where(a => a.Action.StartsWith("License")).ToListAsync();

    [Fact]
    public async Task Activar_PersisteUnaFilaTenantScopedSinPii_YUnSaveChangesPosteriorNoLaAltera()
    {
        _licenses.Resolve(RawKey, Arg.Any<DateOnly>()).Returns(KeyFor(GymCuit));

        await Sut().Handle(new ActivateLicenseCommand(_company.Id, RawKey), CancellationToken.None);

        var row = (await LicenseRowsAsync()).Should().ContainSingle().Subject;
        row.Action.Should().Be("LicenseActivated");
        row.CompanyId.Should().Be(_company.Id);
        row.EntityId.Should().Be(_company.Id);
        row.DiffJson.Should().NotContain(RawKey).And.NotContain("71234567");
        (await _db.Companies.FindAsync(_company.Id))!.LicenseKey.Should().Be(RawKey);

        var snapshot = (row.Id, row.DiffJson, row.Timestamp);

        _company.UpdateBranding(null, "#112233");
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var after = (await LicenseRowsAsync()).Should().ContainSingle().Subject;
        (after.Id, after.DiffJson, after.Timestamp).Should().Be(snapshot);

        _tenant.Set(_other.Id, Guid.NewGuid());
        (await _db.AuditLogs.Where(a => a.Action.StartsWith("License")).ToListAsync())
            .Should().BeEmpty("otro tenant no ve la auditoría de esta empresa");
    }

    [Fact]
    public async Task CuitDistinto_NoActivaLaLicencia_PeroDejaLaFilaDeRechazo()
    {
        _licenses.Resolve(RawKey, Arg.Any<DateOnly>()).Returns(KeyFor("30-69876543-3"));

        var act = () => Sut().Handle(new ActivateLicenseCommand(_company.Id, RawKey), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*otro CUIT*");

        _db.ChangeTracker.Clear();
        (await _db.Companies.FindAsync(_company.Id))!.LicenseKey.Should().BeNull();
        var row = (await LicenseRowsAsync()).Should().ContainSingle().Subject;
        row.Action.Should().Be("LicenseActivationRejected");
        row.CompanyId.Should().Be(_company.Id);
        row.DiffJson.Should().NotContain(RawKey).And.NotContain("69876543").And.NotContain("71234567");
    }
}
