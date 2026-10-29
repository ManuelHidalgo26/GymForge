using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// NotificationLog contra SQLite real: append-only, aislamiento por empresa+sede en las lecturas de la UI
/// y dedup company-wide para el job. Mundo: empresa A con sedes A1 y A2, empresa B con la sede B1.
/// </summary>
public class NotificationLogRepositoryTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private NotificationLogRepository _repo = null!;

    private Company _companyA = null!, _companyB = null!;
    private Site _siteA1 = null!, _siteA2 = null!, _siteB1 = null!;
    private Member _memberA1 = null!, _memberA2 = null!, _memberB1 = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);
        _repo = new NotificationLogRepository(_db);

        _companyA = Company.Create("Gym A", "30-11111111-1");
        _companyB = Company.Create("Gym B", "30-22222222-2");
        _siteA1 = Site.Create(_companyA.Id, "A Centro", "Calle 1");
        _siteA2 = Site.Create(_companyA.Id, "A Norte", "Calle 2");
        _siteB1 = Site.Create(_companyB.Id, "B Centro", "Calle 3");
        await _db.Companies.AddRangeAsync(_companyA, _companyB);
        await _db.Sites.AddRangeAsync(_siteA1, _siteA2, _siteB1);

        _memberA1 = MemberAt(_companyA, _siteA1, "Ana", "40000001");
        _memberA2 = MemberAt(_companyA, _siteA2, "Beto", "40000002");
        _memberB1 = MemberAt(_companyB, _siteB1, "Carla", "40000003");
        await _db.Members.AddRangeAsync(_memberA1, _memberA2, _memberB1);
        await _db.SaveChangesAsync();

        _tenant.Set(_companyA.Id, _siteA1.Id);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private static Member MemberAt(Company company, Site site, string firstName, string dni) =>
        Member.Create(company.Id, site.Id, firstName, "Test", DocumentType.DNI, dni, Gender.Female);

    private static NotificationLog AutomaticFor(Member member, Guid chargeId, int stage) =>
        NotificationLog.Automatic(
            member.CompanyId, member.SiteId, member.Id, chargeId, NotificationChannel.WhatsApp, stage, "+5491122334455");

    private static NotificationLog ManualFor(Member member) =>
        NotificationLog.Manual(
            member.CompanyId, member.SiteId, member.Id, null, NotificationChannel.WhatsApp, null, "+5491122334455");

    private async Task AppendAsync(params NotificationLog[] logs)
    {
        foreach (var log in logs)
            await _repo.AppendAsync(log);
        await _repo.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Append_YLuego_GetByMember_DevuelveElAviso()
    {
        var chargeId = Guid.NewGuid();
        await AppendAsync(AutomaticFor(_memberA1, chargeId, stage: 7));

        var logs = await _repo.GetByMemberAsync(_memberA1.Id);

        var log = logs.Should().ContainSingle().Subject;
        log.MemberId.Should().Be(_memberA1.Id);
        log.ChargeId.Should().Be(chargeId);
        log.StageDaysOverdue.Should().Be(7);
        log.Trigger.Should().Be(NotificationTrigger.Automatic);
        log.Channel.Should().Be(NotificationChannel.WhatsApp);
        log.ToPhone.Should().Be("+5491122334455");
    }

    [Fact]
    public async Task GetByMember_DevuelveDelMasRecienteAlMasAntiguo_ConLimite()
    {
        var first = ManualFor(_memberA1);
        await AppendAsync(first);
        await Task.Delay(20);
        var second = ManualFor(_memberA1);
        await AppendAsync(second);

        (await _repo.GetByMemberAsync(_memberA1.Id)).Select(l => l.Id)
            .Should().Equal(second.Id, first.Id);
        (await _repo.GetByMemberAsync(_memberA1.Id, take: 1)).Select(l => l.Id)
            .Should().Equal(second.Id);
    }

    [Fact]
    public async Task GetLastForMember_DevuelveElMasReciente()
    {
        var first = ManualFor(_memberA1);
        await AppendAsync(first);
        await Task.Delay(20);
        var second = ManualFor(_memberA1);
        await AppendAsync(second);

        (await _repo.GetLastForMemberAsync(_memberA1.Id))!.Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task GetLastForMember_SinAvisos_DevuelveNull()
    {
        (await _repo.GetLastForMemberAsync(_memberA1.Id)).Should().BeNull();
    }

    [Fact]
    public async Task WasSentAsync_True_SoloParaMismoCargoYEtapa()
    {
        var chargeId = Guid.NewGuid();
        await AppendAsync(AutomaticFor(_memberA1, chargeId, stage: 7));

        (await _repo.WasSentAsync(_companyA.Id, chargeId, 7)).Should().BeTrue();
        (await _repo.WasSentAsync(_companyA.Id, chargeId, 15)).Should().BeFalse("es otra etapa del mismo cargo");
        (await _repo.WasSentAsync(_companyA.Id, Guid.NewGuid(), 7)).Should().BeFalse("es otro cargo");
    }

    [Fact]
    public async Task WasSentAsync_EsCompanyWide_IgnoraSedeDelScope()
    {
        var chargeId = Guid.NewGuid();
        await AppendAsync(AutomaticFor(_memberA1, chargeId, stage: 7));   // escrito en la sede A1

        // El job corre sin sede (o desde otra): el dedup igual lo ve.
        using (_tenant.BeginScope(_companyA.Id, _siteA2.Id))
            (await _repo.WasSentAsync(_companyA.Id, chargeId, 7)).Should().BeTrue();

        using (_tenant.BeginScope(_companyA.Id, Guid.Empty))
            (await _repo.WasSentAsync(_companyA.Id, chargeId, 7)).Should().BeTrue();
    }

    [Fact]
    public async Task WasSentAsync_NoCruzaEmpresas()
    {
        var chargeId = Guid.NewGuid();
        await AppendAsync(AutomaticFor(_memberA1, chargeId, stage: 7));

        (await _repo.WasSentAsync(_companyB.Id, chargeId, 7)).Should().BeFalse();
    }

    [Fact]
    public async Task Reads_AreIsolatedByCompanyAndSite()
    {
        await AppendAsync(ManualFor(_memberA1), ManualFor(_memberA2), ManualFor(_memberB1));

        using (_tenant.BeginScope(_companyB.Id, _siteB1.Id))
        {
            (await _repo.GetByMemberAsync(_memberA1.Id)).Should().BeEmpty("el aviso es de otra empresa");
            (await _repo.GetLastForMemberAsync(_memberA1.Id)).Should().BeNull();
            (await _repo.GetByMemberAsync(_memberB1.Id)).Should().ContainSingle();
        }

        // Mismo CompanyId, otra sede: el aviso de A2 no se ve parado en A1.
        (await _repo.GetByMemberAsync(_memberA2.Id)).Should().BeEmpty("el aviso es de otra sede");
        (await _repo.GetByMemberAsync(_memberA1.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task MemberWithReminders_CountsAsHavingActivity_SoItIsNeverHardDeleted()
    {
        var members = new MemberRepository(_db);
        (await members.HasActivityAsync(_memberA1.Id)).Should().BeFalse();

        await AppendAsync(ManualFor(_memberA1));

        (await members.HasActivityAsync(_memberA1.Id)).Should().BeTrue("el log sobrevive al socio (FK Restrict)");
    }

    [Fact]
    public void Contract_IsAppendOnly_NoUpdateNiRemove()
    {
        var methods = typeof(INotificationLogRepository).GetMethods().Select(m => m.Name);

        methods.Should().NotContain(name => name.StartsWith("Update") || name.StartsWith("Remove") || name.StartsWith("Delete"));
    }
}
