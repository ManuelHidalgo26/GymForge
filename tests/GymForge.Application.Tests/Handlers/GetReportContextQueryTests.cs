using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Reports;
using GymForge.Domain.Entities;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class GetReportContextQueryTests
{
    private readonly ISiteRepository _sites = Substitute.For<ISiteRepository>();
    private readonly Company _company = Company.Create("Power Gym", "30-71234567-3");
    private readonly Site _central;

    public GetReportContextQueryTests()
    {
        _central = Site.Create(_company.Id, "Central", "Calle 1");
        _sites.GetCompanyAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(_company);
        _sites.GetSiteAsync(_central.Id, Arg.Any<CancellationToken>()).Returns(_central);
    }

    private Task<ReportContext> RunAsync(Guid companyId, Guid? siteId) =>
        new GetReportContextQueryHandler(_sites).Handle(new GetReportContextQuery(companyId, siteId), CancellationToken.None);

    [Fact]
    public async Task ConSede_TraeLaMarcaDelGimnasioYElNombreDeLaSede()
    {
        _company.UpdateBranding("logo.png", "#10B981");

        var context = await RunAsync(_company.Id, _central.Id);

        context.Branding.Should().Be(new ReportBranding("Power Gym", "30-71234567-3", "Central", "logo.png", "#10B981"));
        context.Scope.Should().Be("Central");
        context.Timezone.Should().Be("Argentina Standard Time");
    }

    [Fact]
    public async Task SinSede_EsTodaLaEmpresa_YElEncabezadoNoLlevaSede()
    {
        var context = await RunAsync(_company.Id, siteId: null);

        context.Branding.SiteName.Should().BeNull();
        context.Scope.Should().Be("Toda la empresa");
    }

    [Fact]
    public async Task SinLogoConfigurado_ElLogoQuedaNulo()
    {
        var context = await RunAsync(_company.Id, _central.Id);

        context.Branding.LogoPath.Should().BeNull();
    }

    [Fact]
    public async Task UnaSedeDeOtraEmpresa_SeTrataComoInexistente()
    {
        var other = Company.Create("Otro Gym", "30-70000000-1");
        var foreignSite = Site.Create(other.Id, "Ajena", "Calle 9");
        _sites.GetSiteAsync(foreignSite.Id, Arg.Any<CancellationToken>()).Returns(foreignSite);

        var act = () => RunAsync(_company.Id, foreignSite.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UnaEmpresaInexistente_Lanza()
    {
        var act = () => RunAsync(Guid.NewGuid(), siteId: null);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public void ToLocal_PasaElInstanteUtcALaHoraDelGimnasio()
    {
        var context = new ReportContext(
            new ReportBranding("Gym", null, null, null, null), "Toda la empresa", "Argentina Standard Time");

        context.ToLocal(new DateTime(2026, 11, 3, 2, 0, 0, DateTimeKind.Utc)).Should().Be(new DateTime(2026, 11, 2, 23, 0, 0));
    }
}
