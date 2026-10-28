using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

public class AmbientCurrentUserTests
{
    private readonly AmbientCurrentUser _user = new();

    [Fact]
    public void WithoutSet_ThereIsNoUser() =>
        _user.StaffId.Should().BeNull();

    [Fact]
    public void Set_DefinesTheUser_AndNullClearsIt()
    {
        var staffId = Guid.NewGuid();

        _user.Set(staffId);
        _user.StaffId.Should().Be(staffId);

        _user.Set(null);
        _user.StaffId.Should().BeNull();
    }

    [Fact]
    public void SystemScope_HidesTheUser_AndRestoresItOnDispose()
    {
        var staffId = Guid.NewGuid();
        _user.Set(staffId);

        using (_user.BeginSystemScope())
            _user.StaffId.Should().BeNull("dentro del scope la operación es del sistema");

        _user.StaffId.Should().Be(staffId);
    }

    [Fact]
    public void SystemScope_DisposedTwice_IsHarmless()
    {
        var staffId = Guid.NewGuid();
        _user.Set(staffId);
        var scope = _user.BeginSystemScope();

        scope.Dispose();
        scope.Dispose();

        _user.StaffId.Should().Be(staffId);
    }

    [Fact]
    public void NestedSystemScopes_StayInSystemUntilTheOutermostEnds()
    {
        _user.Set(Guid.NewGuid());

        using (_user.BeginSystemScope())
        {
            using (_user.BeginSystemScope())
                _user.StaffId.Should().BeNull();

            _user.StaffId.Should().BeNull();
        }

        _user.StaffId.Should().NotBeNull();
    }

    [Fact]
    public async Task SystemScope_DoesNotLeakIntoAConcurrentFlow()
    {
        var staffId = Guid.NewGuid();
        _user.Set(staffId);
        var insideScope = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var systemFlow = Task.Run(async () =>
        {
            using var scope = _user.BeginSystemScope();
            insideScope.SetResult();
            await release.Task;
            return _user.StaffId;
        });
        await insideScope.Task;

        var seenByTheOtherFlow = _user.StaffId;   // p. ej. la UI mientras corre el mantenimiento
        release.SetResult();

        seenByTheOtherFlow.Should().Be(staffId);
        (await systemFlow).Should().BeNull();
    }

    [Fact]
    public async Task Audit_AttributesSystemOperationsToNobody()
    {
        var clock = FakeClock.OnDay(new DateOnly(2026, 5, 10));
        var staffId = Guid.NewGuid();
        _user.Set(staffId);
        await using var db = TestDb.CreateAudited(out var tenant, _user, clock);
        var company = Company.Create("Gym Test", "30-12345678-9");
        var site = Site.Create(company.Id, "Central", "Av. Test 123");
        await db.Companies.AddAsync(company);
        await db.Sites.AddAsync(site);
        await db.SaveChangesAsync();
        tenant.Set(company.Id, site.Id);

        await db.Members.AddAsync(
            Member.Create(company.Id, site.Id, "Ana", "García", DocumentType.DNI, "30111222", Gender.Female));
        await db.SaveChangesAsync();

        using (_user.BeginSystemScope())
        {
            await db.Members.AddAsync(
                Member.Create(company.Id, site.Id, "Eva", "Ruiz", DocumentType.DNI, "30111444", Gender.Female));
            await db.SaveChangesAsync();
        }

        var userIds = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityType == nameof(Member)).Select(a => a.UserId).ToListAsync();
        userIds.Should().BeEquivalentTo(new Guid?[] { staffId, null },
            "el guardado de Ana es del cajero y el de Eva, hecho dentro del scope, es del sistema");
    }
}
