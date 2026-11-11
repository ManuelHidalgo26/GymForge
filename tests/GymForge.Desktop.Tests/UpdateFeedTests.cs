using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Desktop.Services;
using NSubstitute;

namespace GymForge.Desktop.Tests;

public class UpdateFeedTests
{
    [Theory]
    [InlineData("https://updates.example.com/releases")]
    [InlineData(@"\\srv\share")]
    [InlineData(@"C:\releases")]
    [InlineData(@"D:\releases")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowedSource_AceptaHttpsCarpetasYVacio(string? value)
    {
        UpdateFeed.IsAllowedSource(value, out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("http://updates.example.com/releases")]
    [InlineData("ftp://updates.example.com")]
    [InlineData("no es una ruta")]
    public void IsAllowedSource_RechazaHttpYBasura_ConMensajeAccionable(string value)
    {
        UpdateFeed.IsAllowedSource(value, out var error).Should().BeFalse();
        error.Should().Contain("https://");
    }

    [Theory]
    [InlineData("https://override", "https://default", "https://override")]
    [InlineData(" https://override ", "https://default", "https://override")]
    [InlineData("", "https://default", "https://default")]
    [InlineData(null, "https://default", "https://default")]
    [InlineData(null, "", null)]
    [InlineData("  ", null, null)]
    public void Resolve_PriorizaElOverrideSobreElDefaultDeBuild(string? configured, string? buildDefault, string? expected)
    {
        var settings = Substitute.For<ILocalSettings>();
        settings.UpdateFeedUrl.Returns(configured);

        UpdateFeed.Resolve(settings, buildDefault).Should().Be(expected);
    }
}
