using System.Security.Cryptography;
using FluentAssertions;
using GymForge.Application.UseCases.Licensing;

namespace GymForge.Application.Tests.Handlers;

public class LicenseServiceTests
{
    private static readonly DateOnly Today = new(2026, 6, 11);

    private readonly ECDsa _keys = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private LicenseService Sut() =>
        new(Convert.ToBase64String(_keys.ExportSubjectPublicKeyInfo()));

    private string KeyFor(DateOnly expiresOn, int maxSites = 3, int maxMembers = 1000) =>
        LicenseCodec.Encode(new LicensePayload(
            Guid.NewGuid(), "Iron Temple SRL", "30-71234567-8", "Pro",
            maxSites, maxMembers, Today.AddYears(-1), expiresOn), _keys);

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
}
