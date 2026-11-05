using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Licensing;
using GymForge.Domain.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class ActivateLicenseCommandTests
{
    private static readonly DateOnly Today = new(2026, 6, 11);

    // CUITs con dígito verificador correcto.
    private const string GymCuit = "30-71234567-1";
    private const string OtherCuit = "30-69876543-3";

    private readonly ISiteRepository _repo = Substitute.For<ISiteRepository>();
    private readonly ILicenseService _licenses = Substitute.For<ILicenseService>();
    private readonly CurrentLicense _current = new();
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();
    private readonly CapturingLogger<ActivateLicenseCommandHandler> _log = new();

    private ActivateLicenseCommandHandler Sut() => new(_repo, _licenses, _current, _calendar, _log);

    public ActivateLicenseCommandTests() =>
        _calendar.TodayAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Today);

    private Company CompanyWithTaxId(string taxId)
    {
        var company = Company.Create("Iron Temple SRL", taxId);
        _repo.GetCompanyAsync(company.Id, Arg.Any<CancellationToken>()).Returns(company);
        return company;
    }

    private static LicenseState ActiveFor(string? cuit, bool machineMismatch = false) => new(
        LicenseStatus.Active, "Pro", 3, 1000, Today.AddYears(1), "Iron Temple SRL",
        Cuit: cuit, MachineMismatch: machineMismatch);

    private Task<LicenseState> Activate(Company company, LicenseState resolved)
    {
        _licenses.Resolve("GYMF.x.y", Today).Returns(resolved);
        return Sut().Handle(new ActivateLicenseCommand(company.Id, "GYMF.x.y"), CancellationToken.None);
    }

    [Fact]
    public async Task Handle_ClaveValida_PersisteYActualizaElEstadoVivo()
    {
        var company = CompanyWithTaxId(GymCuit);
        var active = ActiveFor(GymCuit);

        var state = await Activate(company, active);

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

    // ── CUIT: bloquea al activar ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_CuitCoincide_Activa()
    {
        var company = CompanyWithTaxId(GymCuit);

        var state = await Activate(company, ActiveFor(GymCuit));

        state.Status.Should().Be(LicenseStatus.Active);
        company.LicenseKey.Should().Be("GYMF.x.y");
    }

    [Fact]
    public async Task Handle_CuitDistinto_RechazaConMensajeClaro_YNoPersisteNada()
    {
        var company = CompanyWithTaxId(GymCuit);

        var act = () => Activate(company, ActiveFor(OtherCuit));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*otro CUIT*{OtherCuit}*{GymCuit}*");
        company.LicenseKey.Should().BeNull();
        _current.State.Status.Should().Be(LicenseStatus.Free);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_ClaveSinCuit_Activa(string? keyCuit)
    {
        var company = CompanyWithTaxId(GymCuit);

        var state = await Activate(company, ActiveFor(keyCuit));

        state.Status.Should().Be(LicenseStatus.Active);
        company.LicenseKey.Should().Be("GYMF.x.y");
    }

    [Theory]
    [InlineData("30712345671", GymCuit)]
    [InlineData(GymCuit, "30712345671")]
    [InlineData(" 30-71234567-1 ", GymCuit)]
    public async Task Handle_CuitConFormatoDistintoPeroMismoNumero_Activa(string keyCuit, string companyCuit)
    {
        var company = CompanyWithTaxId(companyCuit);

        var state = await Activate(company, ActiveFor(keyCuit));

        state.Status.Should().Be(LicenseStatus.Active);
    }

    [Fact]
    public async Task Handle_CuitDeLaClaveConDigitoVerificadorIncorrecto_ActivaYLoRegistra()
    {
        // Typo del vendedor al emitir: no se deja afuera al cliente, pero queda en el log.
        var company = CompanyWithTaxId(GymCuit);

        var state = await Activate(company, ActiveFor("30-71234567-8"));

        state.Status.Should().Be(LicenseStatus.Active);
        _log.Levels.Should().Contain(LogLevel.Warning);
    }

    [Fact]
    public async Task Handle_CuitDeLaEmpresaConDigitoVerificadorIncorrecto_ActivaYLoRegistra()
    {
        var company = CompanyWithTaxId("20-12345678-9");

        var state = await Activate(company, ActiveFor(GymCuit));

        state.Status.Should().Be(LicenseStatus.Active);
        _log.Levels.Should().Contain(LogLevel.Warning);
    }

    [Fact]
    public async Task Handle_CuitCoincide_NoLoguea()
    {
        var company = CompanyWithTaxId(GymCuit);

        await Activate(company, ActiveFor(GymCuit));

        _log.Levels.Should().BeEmpty();
    }

    // ── Máquina: avisa, no bloquea ───────────────────────────────────────────

    [Fact]
    public async Task Handle_ClaveAtadaAOtraPc_ActivaYConservaElAviso()
    {
        var company = CompanyWithTaxId(GymCuit);

        var state = await Activate(company, ActiveFor(GymCuit, machineMismatch: true));

        state.MachineMismatch.Should().BeTrue();
        _current.State.MachineMismatch.Should().BeTrue();
        _current.State.Status.Should().Be(LicenseStatus.Active);
        company.LicenseKey.Should().Be("GYMF.x.y");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
