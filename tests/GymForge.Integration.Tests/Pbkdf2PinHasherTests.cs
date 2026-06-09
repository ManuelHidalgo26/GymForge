using FluentAssertions;
using GymForge.Infrastructure.Services;

namespace GymForge.Integration.Tests;

public class Pbkdf2PinHasherTests
{
    private readonly Pbkdf2PinHasher _hasher = new();

    [Fact]
    public void Hash_ThenVerify_Succeeds()
    {
        var hash = _hasher.Hash("1234");
        _hasher.Verify("1234", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPin_Fails()
    {
        var hash = _hasher.Hash("1234");
        _hasher.Verify("0000", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_SamePinTwice_ProducesDifferentHashes()
    {
        // Salt aleatorio → hashes distintos, ambos verificables.
        _hasher.Hash("1234").Should().NotBe(_hasher.Hash("1234"));
    }

    [Fact]
    public void Verify_MalformedHash_FailsGracefully() =>
        _hasher.Verify("1234", "no-es-un-hash-valido").Should().BeFalse();
}
