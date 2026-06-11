using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Settings;
using GymForge.Domain.Entities;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class SiteLimitTests
{
    private readonly ISiteRepository _repo = Substitute.For<ISiteRepository>();
    private readonly CurrentLicense _license = new();   // arranca en Free: 1 sede

    [Fact]
    public async Task CreateSite_LimiteFreeAlcanzado_Bloquea()
    {
        var companyId = Guid.NewGuid();
        _repo.CountActiveSitesAsync(companyId, Arg.Any<CancellationToken>()).Returns(1);

        var act = () => new CreateSiteCommandHandler(_repo, _license).Handle(
            new CreateSiteCommand(companyId, "Sede Sur", "Av. Rivadavia 5000"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*1 sede*");
        await _repo.DidNotReceive().AddSiteAsync(Arg.Any<Site>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateSite_ConLicenciaPro_Permite()
    {
        _license.State = new LicenseState(LicenseStatus.Active, "Pro", 3, 1000, null, null);
        var companyId = Guid.NewGuid();
        _repo.CountActiveSitesAsync(companyId, Arg.Any<CancellationToken>()).Returns(1);

        var dto = await new CreateSiteCommandHandler(_repo, _license).Handle(
            new CreateSiteCommand(companyId, "Sede Sur", "Av. Rivadavia 5000"), CancellationToken.None);

        dto.Name.Should().Be("Sede Sur");
        await _repo.Received(1).AddSiteAsync(Arg.Any<Site>(), Arg.Any<CancellationToken>());
    }
}
