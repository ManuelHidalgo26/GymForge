using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Licensing;
using GymForge.Domain.Entities;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class ActivateLicenseCommandTests
{
    private static readonly DateOnly Today = new(2026, 6, 11);

    private readonly ISiteRepository _repo = Substitute.For<ISiteRepository>();
    private readonly ILicenseService _licenses = Substitute.For<ILicenseService>();
    private readonly CurrentLicense _current = new();
    private readonly IClock _clock = Substitute.For<IClock>();

    private ActivateLicenseCommandHandler Sut() => new(_repo, _licenses, _current, _clock);

    public ActivateLicenseCommandTests() => _clock.Today.Returns(Today);

    [Fact]
    public async Task Handle_ClaveValida_PersisteYActualizaElEstadoVivo()
    {
        var company = Company.Create("Iron Temple SRL", "30-71234567-8");
        _repo.GetCompanyAsync(company.Id, Arg.Any<CancellationToken>()).Returns(company);

        var active = new LicenseState(
            LicenseStatus.Active, "Pro", 3, 1000, Today.AddYears(1), "Iron Temple SRL");
        _licenses.Resolve("GYMF.x.y", Today).Returns(active);

        var state = await Sut().Handle(
            new ActivateLicenseCommand(company.Id, "GYMF.x.y"), CancellationToken.None);

        state.Should().Be(active);
        company.LicenseKey.Should().Be("GYMF.x.y");
        _current.State.Should().Be(active);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClaveInvalida_NoPersisteNada()
    {
        _licenses.Resolve("basura", Today).Returns(LicenseState.Free);

        var act = () => Sut().Handle(
            new ActivateLicenseCommand(Guid.NewGuid(), "basura"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no es válida*");
        _current.State.Status.Should().Be(LicenseStatus.Free);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClaveVencida_AvisaQueRenueve()
    {
        _licenses.Resolve("GYMF.vieja", Today).Returns(new LicenseState(
            LicenseStatus.Expired, "Pro", 1, 50, Today.AddMonths(-2), "Iron Temple SRL"));

        var act = () => Sut().Handle(
            new ActivateLicenseCommand(Guid.NewGuid(), "GYMF.vieja"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*venció*");
    }
}
