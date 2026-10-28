using FluentAssertions;
using GymForge.Application.UseCases.Licensing;

namespace GymForge.Application.Tests.Licensing;

/// <summary>
/// Texto de uso de la licencia en Configuración: no debe mostrar "Sedes: 2 de 1" a secas
/// cuando hay más sedes/socios que el tope (downgrade o vencimiento a Free con datos preexistentes).
/// </summary>
public class LicenseUsageTests
{
    private static readonly LicenseState Pro = new(
        LicenseStatus.Active, "Pro", MaxSites: 3, MaxMembers: 1000, new DateOnly(2027, 1, 1), "Iron Temple");

    [Fact]
    public void Sites_DentroDelPlan_NoMarcaExceso()
    {
        var line = LicenseUsage.Sites(1, LicenseState.Free);

        line.OverLimit.Should().BeFalse();
        line.Display.Should().NotContain("excede");
    }

    [Fact]
    public void Sites_JustoEnElTope_NoMarcaExceso()
    {
        LicenseUsage.Sites(3, Pro).OverLimit.Should().BeFalse();
    }

    [Fact]
    public void Sites_SobreElPlan_MarcaExcesoYLoDice()
    {
        var line = LicenseUsage.Sites(2, LicenseState.Free);

        line.OverLimit.Should().BeTrue();
        line.Display.Should().Contain("excede el plan");
    }

    [Fact]
    public void Members_SobreElPlan_MarcaExceso()
    {
        var line = LicenseUsage.Members(60, LicenseState.Free);

        line.OverLimit.Should().BeTrue();
        line.Display.Should().Be("Socios: 60 de 50 (excede el plan)");
    }

    [Fact]
    public void Members_BajoElPlan_NoMarcaExceso()
    {
        var line = LicenseUsage.Members(40, LicenseState.Free);

        line.OverLimit.Should().BeFalse();
        line.Display.Should().Be("Socios: 40 de 50");
    }

    [Fact]
    public void Display_FormateaEtiquetaUsadoYMaximo()
    {
        LicenseUsage.Sites(1, LicenseState.Free).Display.Should().Be("Sedes: 1 de 1");
        LicenseUsage.Sites(2, LicenseState.Free).Display.Should().Be("Sedes: 2 de 1 (excede el plan)");
        LicenseUsage.Members(250, Pro).Display.Should().Be("Socios: 250 de 1000");
    }

    [Fact]
    public void SinUso_NoMarcaExceso()
    {
        LicenseUsage.Members(0, LicenseState.Free).OverLimit.Should().BeFalse();
        LicenseUsage.Sites(0, LicenseState.Free).OverLimit.Should().BeFalse();
    }
}
