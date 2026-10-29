using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Licensing;
using GymForge.Desktop.Services;
using GymForge.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GymForge.Desktop.Tests;

/// <summary>Arma una <see cref="SessionContext"/> ya inicializada contra repositorios falsos (empresa + sedes).</summary>
internal static class TestSession
{
    public static async Task<SessionContext> InitializedAsync(
        Company company, IReadOnlyList<Site> sites, DunningConfig? dunning = null)
    {
        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetCompaniesAsync(Arg.Any<CancellationToken>()).Returns(new List<Company> { company });
        siteRepo.GetByCompanyAsync(company.Id, Arg.Any<CancellationToken>()).Returns(sites.ToList());
        var staffRepo = Substitute.For<IStaffRepository>();
        staffRepo.GetActiveByCompanyAsync(company.Id, Arg.Any<CancellationToken>()).Returns(new List<Staff>());
        var licenses = Substitute.For<ILicenseService>();
        licenses.Resolve(Arg.Any<string?>(), Arg.Any<DateOnly>()).Returns(LicenseState.Free);

        var services = new ServiceCollection();
        services.AddSingleton(siteRepo);
        services.AddSingleton(staffRepo);
        var provider = services.BuildServiceProvider();

        var session = new SessionContext(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new GatekeeperConfig(), dunning ?? new DunningConfig(), new CurrentLicense(), licenses);
        await session.InitializeAsync();
        return session;
    }
}
