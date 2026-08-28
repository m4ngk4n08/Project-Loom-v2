using Loom.Security;
using Xunit;

namespace Loom.Telemetry.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_RoundTripsThroughTryParse()
    {
        var encoded = PasswordHasher.Hash("correct horse battery staple");
        var parsed = PasswordHasher.TryParse(encoded, out var iterations, out var salt, out var hash);

        Assert.True(parsed);
        Assert.Equal(PasswordHasher.Iterations, iterations);
        Assert.Equal(PasswordHasher.SaltBytes, salt.Length);
        Assert.Equal(PasswordHasher.HashBytes, hash.Length);
    }

    [Fact]
    public void Verify_WithCorrectPassword_ReturnsTrue()
    {
        var encoded = PasswordHasher.Hash("hunter2");
        PasswordHasher.TryParse(encoded, out var iterations, out var salt, out var hash);

        Assert.True(PasswordHasher.Verify("hunter2", iterations, salt, hash));
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFalse()
    {
        var encoded = PasswordHasher.Hash("hunter2");
        PasswordHasher.TryParse(encoded, out var iterations, out var salt, out var hash);

        Assert.False(PasswordHasher.Verify("wrong", iterations, salt, hash));
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentOutput()
    {
        var first = PasswordHasher.Hash("same-password");
        var second = PasswordHasher.Hash("same-password");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("a$b$c")]
    [InlineData("md5$1$x$y")]
    [InlineData("")]
    public void TryParse_OnMalformedInput_ReturnsFalseWithoutThrowing(string input)
    {
        var parsed = PasswordHasher.TryParse(input, out _, out _, out _);
        Assert.False(parsed);
    }
}
