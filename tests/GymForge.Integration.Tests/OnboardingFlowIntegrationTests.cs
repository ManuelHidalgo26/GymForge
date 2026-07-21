using FluentAssertions;
using GymForge.Application.UseCases.Onboarding;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using GymForge.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// Primer arranque (handler + repos + EF reales): crea el gimnasio real sobre una
/// base limpia, como haría el asistente de onboarding en la instalación de un cliente.
/// </summary>
public class OnboardingFlowIntegrationTests : IAsyncLifetime
{
    private const string ValidCuit = "20-12345678-6";
    private GymForgeDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<GymForgeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new GymForgeDbContext(opts);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private CompleteOnboardingCommandHandler NewHandler() => new(
        new SiteRepository(_db), new MembershipTypeRepository(_db), new Pbkdf2PinHasher());

    [Fact]
    public async Task Complete_CreatesGymSiteAdminAndSamplePlans()
    {
        var companyId = await NewHandler().Handle(new CompleteOnboardingCommand(
            "PowerGym", ValidCuit, "Sede Central", "Av. Test 123",
            "Lucía", "Fernández", "4321", "#059669", CreateSamplePlans: true),
            CancellationToken.None);

        var company = await _db.Companies.FindAsync(companyId);
        company.Should().NotBeNull();
        company!.BrandColorHex.Should().Be("#059669");

        (await _db.Sites.CountAsync(s => s.CompanyId == companyId)).Should().Be(1);
        (await _db.Staff.CountAsync(s => s.CompanyId == companyId)).Should().Be(1);
        (await _db.MembershipTypes.CountAsync(m => m.CompanyId == companyId)).Should().Be(3);

        var admin = await _db.Staff.FirstAsync(s => s.CompanyId == companyId);
        admin.FirstName.Should().Be("Lucía");
        // El PIN se guarda hasheado, nunca en claro.
        admin.PinCodeHash.Should().NotBeNullOrEmpty();
        admin.PinCodeHash.Should().NotBe("4321");
    }

    [Fact]
    public async Task Complete_WithoutSamplePlans_CreatesNoPlans()
    {
        var companyId = await NewHandler().Handle(new CompleteOnboardingCommand(
            "MiniGym", ValidCuit, "Central", "Calle 1",
            "Juan", "Pérez", "1111", "#6366F1", CreateSamplePlans: false),
            CancellationToken.None);

        (await _db.MembershipTypes.CountAsync(m => m.CompanyId == companyId)).Should().Be(0);
    }

    [Fact]
    public async Task Complete_WithInvalidCuit_Throws()
    {
        var act = async () => await NewHandler().Handle(new CompleteOnboardingCommand(
            "BadGym", "20-12345678-9", "Central", "Calle 1",
            "Juan", "Pérez", "1111", "#6366F1", CreateSamplePlans: false),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
