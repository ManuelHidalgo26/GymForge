using System.Security.Cryptography;
using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Licensing;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class LicenseServiceTests
{
    private static readonly DateOnly Today = new(2026, 6, 11);
    private const string ThisPc = "A1B2-C3D4-E5F6-0718";
    private const string OtherPc = "FFFF-FFFF-FFFF-FFFF";

    private readonly ECDsa _keys = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private LicenseService Sut() =>
        new(Convert.ToBase64String(_keys.ExportSubjectPublicKeyInfo()));

    private LicenseService SutOnPc(string currentFingerprint)
    {
        var fingerprint = Substitute.For<IMachineFingerprint>();
        fingerprint.Current().Returns(currentFingerprint);
        return new LicenseService(Convert.ToBase64String(_keys.ExportSubjectPublicKeyInfo()), fingerprint);
    }

    private string KeyFor(DateOnly expiresOn, int maxSites = 3, int maxMembers = 1000, string? machineId = null) =>
        LicenseCodec.Encode(new LicensePayload(
            Guid.NewGuid(), "Iron Temple SRL", "30-71234567-1", "Pro",
            maxSites, maxMembers, Today.AddYears(-1), expiresOn, machineId), _keys);

    [Fact]
    public void Resolve_SinClave_EsFree()
    {
        var state = Sut().Resolve(null, Today);

        state.Status.Should().Be(LicenseStatus.Free);
        state.MaxSites.Should().Be(LicenseState.FreeMaxSites);
        state.MaxMembers.Should().Be(LicenseState.FreeMaxMembers);
    }

    [Fact]
    public void Resolve_ClaveValidaVigente_EsActivaConSusLimites()
    {
        var state = Sut().Resolve(KeyFor(Today.AddMonths(6)), Today);

        state.Status.Should().Be(LicenseStatus.Active);
        state.Tier.Should().Be("Pro");
        state.MaxSites.Should().Be(3);
        state.MaxMembers.Should().Be(1000);
        state.GymName.Should().Be("Iron Temple SRL");
    }

    [Fact]
    public void Resolve_ClaveAdulterada_EsFree()
    {
        var key = KeyFor(Today.AddMonths(6));
        // Adulterar el payload (subir el límite a mano) invalida la firma.
        var parts = key.Split('.');
        var tampered = $"{parts[0]}.{parts[1][..^2]}AA.{parts[2]}";

        Sut().Resolve(tampered, Today).Status.Should().Be(LicenseStatus.Free);
    }

    [Fact]
    public void Resolve_ClaveDeOtroVendedor_EsFree()
    {
        using var otherKeys = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var foreignKey = LicenseCodec.Encode(new LicensePayload(
            Guid.NewGuid(), "Pirata SA", "", "Pro", 99, 99999, Today, Today.AddYears(10)), otherKeys);

        Sut().Resolve(foreignKey, Today).Status.Should().Be(LicenseStatus.Free);
    }

    [Fact]
    public void Resolve_VencidaDentroDeGracia_SigueOperativa()
    {
        var state = Sut().Resolve(KeyFor(Today.AddDays(-5)), Today);

        state.Status.Should().Be(LicenseStatus.Grace);
        state.MaxMembers.Should().Be(1000);   // conserva los límites pagos
        state.IsPaid.Should().BeTrue();
    }

    [Fact]
    public void Resolve_VencidaFueraDeGracia_DegradaALimitesFree()
    {
        var state = Sut().Resolve(KeyFor(Today.AddDays(-(LicenseService.GraceDays + 1))), Today);

        state.Status.Should().Be(LicenseStatus.Expired);
        state.MaxSites.Should().Be(LicenseState.FreeMaxSites);
        state.MaxMembers.Should().Be(LicenseState.FreeMaxMembers);
        state.IsPaid.Should().BeFalse();
    }

    [Fact]
    public void Resolve_BasuraOClaveMalFormada_EsFree()
    {
        Sut().Resolve("no-es-una-clave", Today).Status.Should().Be(LicenseStatus.Free);
        Sut().Resolve("GYMF.abc", Today).Status.Should().Be(LicenseStatus.Free);
        Sut().Resolve("GYMF.!!!.???", Today).Status.Should().Be(LicenseStatus.Free);
    }

    // ── CUIT de la clave ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_ExponeElCuitDeLaClave()
    {
        Sut().Resolve(KeyFor(Today.AddMonths(6)), Today).Cuit.Should().Be("30-71234567-1");
    }

    [Fact]
    public void Resolve_SinLicencia_NoTieneCuit()
    {
        Sut().Resolve(null, Today).Cuit.Should().BeNull();
    }

    // ── Atado a la máquina: avisa, no bloquea ────────────────────────────────

    [Fact]
    public void Resolve_MachineIdCoincide_SinMismatch()
    {
        var state = SutOnPc(ThisPc).Resolve(KeyFor(Today.AddMonths(6), machineId: ThisPc), Today);

        state.Status.Should().Be(LicenseStatus.Active);
        state.MachineMismatch.Should().BeFalse();
    }

    [Fact]
    public void Resolve_MachineIdCoincideSinImportarMayusculas_SinMismatch()
    {
        var state = SutOnPc(ThisPc.ToLowerInvariant()).Resolve(KeyFor(Today.AddMonths(6), machineId: ThisPc), Today);

        state.MachineMismatch.Should().BeFalse();
    }

    [Fact]
    public void Resolve_MachineIdDistinto_MarcaMismatchPeroSigueActive()
    {
        var state = SutOnPc(OtherPc).Resolve(KeyFor(Today.AddMonths(6), machineId: ThisPc), Today);

        state.MachineMismatch.Should().BeTrue();
        state.Status.Should().Be(LicenseStatus.Active);   // no degrada a Free
        state.IsPaid.Should().BeTrue();
        state.MaxSites.Should().Be(3);
        state.MaxMembers.Should().Be(1000);
    }

    [Fact]
    public void Resolve_MachineIdDistintoEnGracia_ConservaLaGraciaYMarcaMismatch()
    {
        var state = SutOnPc(OtherPc).Resolve(KeyFor(Today.AddDays(-5), machineId: ThisPc), Today);

        state.Status.Should().Be(LicenseStatus.Grace);
        state.MachineMismatch.Should().BeTrue();
    }

    [Fact]
    public void Resolve_PayloadSinMachineId_NuncaMarcaMismatch()
    {
        var state = SutOnPc(OtherPc).Resolve(KeyFor(Today.AddMonths(6)), Today);

        state.MachineMismatch.Should().BeFalse();
    }

    [Fact]
    public void Resolve_SinFingerprintInyectado_NuncaMarcaMismatch()
    {
        var state = Sut().Resolve(KeyFor(Today.AddMonths(6), machineId: ThisPc), Today);

        state.MachineMismatch.Should().BeFalse();
    }

    [Fact]
    public void Resolve_FingerprintNoDisponible_NoMarcaMismatch()
    {
        // Si esta PC no pudo calcular su código ("") no hay con qué comparar: no se molesta al cliente.
        var state = SutOnPc(string.Empty).Resolve(KeyFor(Today.AddMonths(6), machineId: ThisPc), Today);

        state.MachineMismatch.Should().BeFalse();
    }

    [Fact]
    public void Resolve_ClaveInvalida_NoMarcaMismatch()
    {
        SutOnPc(OtherPc).Resolve("no-es-una-clave", Today).MachineMismatch.Should().BeFalse();
    }
}
