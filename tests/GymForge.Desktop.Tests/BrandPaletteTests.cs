using Avalonia.Media;
using FluentAssertions;
using GymForge.Desktop.Services;

namespace GymForge.Desktop.Tests;

public class BrandPaletteTests
{
    private const double AaNormalText = 4.5;

    [Fact]
    public void Tint_KeepsTheAccentRgbWithTheFixedAlphaOfEachTheme()
    {
        var palette = BrandPalette.For(Color.Parse("#22C55E"));

        palette.Light.Tint.Should().Be(Color.FromArgb(0x1F, 0x22, 0xC5, 0x5E));
        palette.Dark.Tint.Should().Be(Color.FromArgb(0x2E, 0x22, 0xC5, 0x5E));
    }

    [Theory]
    [InlineData("#6366F1")] // indigo por defecto
    [InlineData("#22C55E")] // verde
    [InlineData("#F97316")] // naranja
    [InlineData("#FACC15")] // amarillo (claro)
    [InlineData("#FFFFFF")] // blanco
    [InlineData("#000000")] // negro
    [InlineData("#7DD3FC")] // celeste pastel
    public void AccentText_MeetsAaOnItsOwnTintInBothThemesOnEverySurface(string hex)
    {
        var palette = BrandPalette.For(Color.Parse(hex));

        AssertAaOnAllSurfaces(palette.Light, BrandPalette.LightSurfaces);
        AssertAaOnAllSurfaces(palette.Dark, BrandPalette.DarkSurfaces);
    }

    [Fact]
    public void AccentText_KeepsTheAccentWhenItIsAlreadyReadable()
    {
        var darkRed = Color.Parse("#7F1D1D");

        BrandPalette.For(darkRed).Light.Text.Should().Be(darkRed);
    }

    [Fact]
    public void AccentText_DarkensInLightAndLightensInDark()
    {
        var yellow = Color.Parse("#FACC15");
        var palette = BrandPalette.For(yellow);

        BrandPalette.Luminance(palette.Light.Text).Should().BeLessThan(BrandPalette.Luminance(yellow));
        BrandPalette.Luminance(palette.Dark.Text).Should().BeGreaterThanOrEqualTo(BrandPalette.Luminance(yellow));
    }

    [Theory]
    [InlineData("#6366F1", "#FFFFFF")]
    [InlineData("#1D4ED8", "#FFFFFF")]
    [InlineData("#FACC15", "#111827")]
    [InlineData("#22C55E", "#111827")]
    public void OnAccent_PicksWhiteWhenItHoldsAndDarkOtherwise(string accentHex, string expectedHex)
    {
        BrandPalette.For(Color.Parse(accentHex)).OnAccent.Should().Be(Color.Parse(expectedHex));
    }

    [Theory]
    [InlineData("#6366F1")]
    [InlineData("#zzz")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_FallsBackToDefaultIndigoWhenTheHexIsInvalid(string? hex)
    {
        var expected = Color.Parse(SessionContext.DefaultBrandColorHex);

        BrandPalette.Resolve(hex).Should().Be(expected);
    }

    [Fact]
    public void Resolve_ParsesAValidHex()
    {
        BrandPalette.Resolve("#22C55E").Should().Be(Color.Parse("#22C55E"));
    }

    [Fact]
    public void ContrastRatio_MatchesWcagReferenceValues()
    {
        BrandPalette.ContrastRatio(Colors.Black, Colors.White).Should().BeApproximately(21.0, 0.01);
        BrandPalette.ContrastRatio(Colors.White, Colors.White).Should().BeApproximately(1.0, 0.001);
    }

    private static void AssertAaOnAllSurfaces(BrandThemeColors theme, IReadOnlyList<Color> surfaces)
    {
        foreach (var surface in surfaces)
        {
            var background = BrandPalette.Composite(theme.Tint, surface);
            BrandPalette.ContrastRatio(theme.Text, background).Should().BeGreaterThanOrEqualTo(AaNormalText);
        }
    }
}
