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

    [Fact]
    public void ForLog_QuitaUserinfoRutaYQuery()
    {
        var result = UpdateFeed.ForLog("https://user:pass@host/path?x=1");

        result.Should().Be("https://host");
        result.Should().NotContain("user").And.NotContain("pass").And.NotContain("path").And.NotContain("x=1");
    }

    [Fact]
    public void ForLog_QuitaUserinfoTambienEnHttp()
    {
        UpdateFeed.ForLog("http://user:pass@host:8080/p").Should().Be("http://host");
    }

    [Fact]
    public void ForLog_UrlSinCredenciales_QuedaIgual()
    {
        UpdateFeed.ForLog("https://host").Should().Be("https://host");
    }

    [Theory]
    [InlineData(@"\\srv\share\secreto", "(UNC)")]
    [InlineData(@"C:\releases\secreto", "(ruta local)")]
    [InlineData(null, "(sin configurar)")]
    [InlineData("", "(sin configurar)")]
    [InlineData("   ", "(sin configurar)")]
    [InlineData("user:pass@host/ruta", "(origen no reconocido)")]
    public void ForLog_RutasYVacio_DevuelvenMarcadorSinLaRuta(string? value, string expected)
    {
        var result = UpdateFeed.ForLog(value);

        result.Should().Be(expected);
        result.Should().NotContain("secreto").And.NotContain("user").And.NotContain("pass");
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
