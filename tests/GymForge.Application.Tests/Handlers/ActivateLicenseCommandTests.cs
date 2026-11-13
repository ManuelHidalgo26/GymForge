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

    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly Guid _staffId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 6, 11, 15, 0, 0, DateTimeKind.Utc);
    private readonly List<AuditLog> _audits = [];

    private ActivateLicenseCommandHandler Sut() =>
        new(_repo, _licenses, _current, _calendar, _user, _clock, _log);

    public ActivateLicenseCommandTests()
    {
        _calendar.TodayAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Today);
        _user.StaffId.Returns(_staffId);
        _clock.Now.Returns(_now);
        _repo.AppendAuditLogAsync(Arg.Do<AuditLog>(_audits.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

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
        await _repo.Received(1).AppendAuditLogAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>());
    }

    // ── Auditoría (ADR-016) ──────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ActivacionOk_DejaUnaFilaDeAuditoriaSinPii()
    {
        var company = CompanyWithTaxId(GymCuit);

        await Activate(company, ActiveFor(GymCuit));

        var audit = _audits.Should().ContainSingle().Subject;
        audit.EntityType.Should().Be("Company");
        audit.EntityId.Should().Be(company.Id);
        audit.CompanyId.Should().Be(company.Id);
        audit.Action.Should().Be("LicenseActivated");
        audit.UserId.Should().Be(_staffId);
        audit.Timestamp.Should().Be(_now);
        audit.DiffJson.Should().Contain("\"tier\":\"Pro\"")
            .And.Contain("\"expiresOn\":\"2027-06-11\"")
            .And.Contain("\"renewal\":false")
            .And.NotContain("GYMF.x.y")
            .And.NotContain(GymCuit)
            .And.NotContain("30712345671");
    }

    [Fact]
    public async Task Handle_Renovacion_MarcaRenewalTrue()
    {
        var company = CompanyWithTaxId(GymCuit);
        company.ActivateLicense("GYMF.vieja.firma");

        await Activate(company, ActiveFor(GymCuit));

        _audits.Should().ContainSingle().Which.DiffJson.Should().Contain("\"renewal\":true");
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
        await _repo.DidNotReceive().AppendAuditLogAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClaveVencida_AvisaQueRenueve()
    {
        _licenses.Resolve("GYMF.vieja", Today).Returns(new LicenseState(
            LicenseStatus.Expired, "Pro", 1, 50, Today.AddMonths(-2), "Iron Temple SRL"));

        var act = () => Sut().Handle(
            new ActivateLicenseCommand(Guid.NewGuid(), "GYMF.vieja"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*venció*");
        await _repo.DidNotReceive().AppendAuditLogAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>());
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
    public async Task Handle_CuitDistinto_RechazaConMensajeClaro_YAuditaElRechazoSinPii()
    {
        var company = CompanyWithTaxId(GymCuit);

        var act = () => Activate(company, ActiveFor(OtherCuit));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*otro CUIT*{OtherCuit}*{GymCuit}*");
        company.LicenseKey.Should().BeNull();
        _current.State.Status.Should().Be(LicenseStatus.Free);
        var audit = _audits.Should().ContainSingle().Subject;
        audit.Action.Should().Be("LicenseActivationRejected");
        audit.CompanyId.Should().Be(company.Id);
        audit.UserId.Should().Be(_staffId);
        audit.DiffJson.Should().Contain("\"reason\":\"CuitMismatch\"")
            .And.Contain("\"keyTier\":\"Pro\"")
            .And.NotContain(OtherCuit).And.NotContain(GymCuit)
            .And.NotContain("30698765433").And.NotContain("GYMF.x.y");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CuitDistinto_SiFallaLaAuditoria_LogueaYRechazaIgual()
    {
        var company = CompanyWithTaxId(GymCuit);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("db caída"));

        var act = () => Activate(company, ActiveFor(OtherCuit));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*otro CUIT*");
        _log.Levels.Should().Contain(LogLevel.Warning);
        company.LicenseKey.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CuitNoComparable_AuditaComoActivacionNormal()
    {
        var company = CompanyWithTaxId(GymCuit);

        await Activate(company, ActiveFor("30-71234567-8"));

        _audits.Should().ContainSingle().Which.Action.Should().Be("LicenseActivated");
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
