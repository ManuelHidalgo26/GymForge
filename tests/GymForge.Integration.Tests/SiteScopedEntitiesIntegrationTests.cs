using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// Rutinas, reservas, mediciones y registros de entrenamiento se aíslan por sede (ISiteScoped):
/// lo cargado en la sede A no aparece con el tenant ambiente en la sede B de la misma empresa.
/// </summary>
public class SiteScopedEntitiesIntegrationTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private Company _company = null!;
    private Site _siteA = null!, _siteB = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);
        _company = Company.Create("Gym", "30-11111111-1");
        _siteA = Site.Create(_company.Id, "Centro", "Calle 1");
        _siteB = Site.Create(_company.Id, "Norte", "Calle 2");
        await _db.Companies.AddAsync(_company);
        await _db.Sites.AddRangeAsync(_siteA, _siteB);

        var memberA = Member.Create(_company.Id, _siteA.Id, "Ana", "Lopez", DocumentType.DNI, "30111222", Gender.Female);
        var memberB = Member.Create(_company.Id, _siteB.Id, "Beto", "Perez", DocumentType.DNI, "30111333", Gender.Male);
        await _db.Members.AddRangeAsync(memberA, memberB);

        var classA = ClassDescription.Create(_company.Id, "Funcional");
        await _db.ClassDescriptions.AddAsync(classA);
        var scheduleA = ClassSchedule.Create(_company.Id, _siteA.Id, classA.Id,
            new DateTime(2026, 6, 17, 18, 0, 0), new DateTime(2026, 6, 17, 19, 0, 0), 10);
        var scheduleB = ClassSchedule.Create(_company.Id, _siteB.Id, classA.Id,
            new DateTime(2026, 6, 17, 18, 0, 0), new DateTime(2026, 6, 17, 19, 0, 0), 10);
        await _db.ClassSchedules.AddRangeAsync(scheduleA, scheduleB);

        await SeedAsync(memberA, _siteA, scheduleA);
        await SeedAsync(memberB, _siteB, scheduleB);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private async Task SeedAsync(Member member, Site site, ClassSchedule schedule)
    {
        await _db.Routines.AddAsync(Routine.Create(
            _company.Id, site.Id, member.Id, "Full", WorkoutGoal.Strength, new DateOnly(2026, 6, 17), 3));
        await _db.WorkoutLogs.AddAsync(WorkoutLog.Create(
            _company.Id, site.Id, member.Id, Guid.NewGuid(), new DateTime(2026, 6, 17), "[]", "[]", "[]"));
        await _db.BodyMeasurements.AddAsync(BodyMeasurement.Create(
            _company.Id, site.Id, member.Id, new DateTime(2026, 6, 17), BodyMeasurementMethod.Manual));
        await _db.Bookings.AddAsync(Booking.Create(_company.Id, site.Id, member.Id, schedule.Id));
    }

    [Fact]
    public async Task EachSiteSeesOnlyItsOwnRoutinesBookingsMeasurementsAndWorkoutLogs()
    {
        _tenant.Set(_company.Id, _siteA.Id);

        (await _db.Routines.ToListAsync()).Should().ContainSingle().Which.SiteId.Should().Be(_siteA.Id);
        (await _db.Bookings.ToListAsync()).Should().ContainSingle().Which.SiteId.Should().Be(_siteA.Id);
        (await _db.BodyMeasurements.ToListAsync()).Should().ContainSingle().Which.SiteId.Should().Be(_siteA.Id);
        (await _db.WorkoutLogs.ToListAsync()).Should().ContainSingle().Which.SiteId.Should().Be(_siteA.Id);

        _tenant.Set(_company.Id, _siteB.Id);

        (await _db.Routines.ToListAsync()).Should().ContainSingle().Which.SiteId.Should().Be(_siteB.Id);
        (await _db.Bookings.ToListAsync()).Should().ContainSingle().Which.SiteId.Should().Be(_siteB.Id);
    }

    [Fact]
    public async Task ARoutineOfSiteA_IsInvisibleFromSiteB_ButStillThereWithoutFilters()
    {
        _tenant.Set(_company.Id, _siteB.Id);
        var routineOfA = await _db.Routines.IgnoreQueryFilters().SingleAsync(r => r.SiteId == _siteA.Id);

        (await _db.Routines.AnyAsync(r => r.Id == routineOfA.Id)).Should().BeFalse();
    }
}
