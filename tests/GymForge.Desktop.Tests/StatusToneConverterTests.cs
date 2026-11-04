using System.Globalization;
using FluentAssertions;
using GymForge.Desktop.Converters;
using GymForge.Domain.Enums;

namespace GymForge.Desktop.Tests;

public class StatusToneConverterTests
{
    private static readonly string[] Tones = ["success", "warning", "danger", "info", "neutral", "accent"];

    private static string ToneOf(object? value) =>
        Tones.Single(tone => (bool)new StatusToneConverter().Convert(value, typeof(bool), tone, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(ChargeStatus.Paid, "success")]
    [InlineData(ChargeStatus.Pending, "warning")]
    [InlineData(ChargeStatus.Overdue, "warning")]
    [InlineData(ChargeStatus.WrittenOff, "neutral")]
    [InlineData(ChargeStatus.Draft, "neutral")]
    [InlineData(ChargeStatus.Refunded, "danger")]
    public void ChargeStatus_MapsToItsTone(ChargeStatus status, string expected) =>
        ToneOf(status).Should().Be(expected);

    [Theory]
    [InlineData(true, "success")]
    [InlineData(false, "neutral")]
    public void ActiveFlag_MapsToSuccessOrNeutral(bool active, string expected) =>
        ToneOf(active).Should().Be(expected);

    [Theory]
    [InlineData(MemberStatus.Active, "success")]
    [InlineData(MemberStatus.Frozen, "info")]
    [InlineData(MemberStatus.Prospect, "accent")]
    [InlineData(MemberStatus.Cancelled, "neutral")]
    public void MemberStatus_KeepsItsTone(MemberStatus status, string expected) =>
        ToneOf(status).Should().Be(expected);

    [Theory]
    [InlineData(MembershipStatus.Overdue, "warning")]
    [InlineData(MembershipStatus.Trial, "accent")]
    public void MembershipStatus_KeepsItsTone(MembershipStatus status, string expected) =>
        ToneOf(status).Should().Be(expected);

    [Fact]
    public void EveryChargeStatus_FallsInExactlyOneTone()
    {
        foreach (var status in Enum.GetValues<ChargeStatus>())
            Tones.Count(tone => (bool)new StatusToneConverter().Convert(status, typeof(bool), tone, CultureInfo.InvariantCulture))
                .Should().Be(1, $"{status} debe caer en un solo tono");
    }

    [Fact]
    public void UnknownValueOrParameter_IsNeutralAndNeverMatchesAMissingTone()
    {
        ToneOf(null).Should().Be("neutral");
        new StatusToneConverter().Convert(ChargeStatus.Paid, typeof(bool), null, CultureInfo.InvariantCulture).Should().Be(false);
    }
}
