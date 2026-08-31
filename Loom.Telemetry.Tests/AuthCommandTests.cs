using Loom.DevTools.Commands;
using Loom.Security;
using Xunit;

namespace Loom.Telemetry.Tests;

public class AuthCommandTests
{
    [Theory]
    [InlineData("﻿secret123", "secret123")]
    [InlineData("secret123", "secret123")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" secret123 ", " secret123 ")]
    [InlineData("﻿", "")]
    [InlineData("secret﻿123", "secret﻿123")]
    public void NormalizePipedPassword_StripsOnlyALeadingBom(string? input, string expected)
    {
        Assert.Equal(expected, AuthCommand.NormalizePipedPassword(input));
    }

    [Theory]
    [InlineData("metrics", JwtScope.Metrics)]
    [InlineData("full", JwtScope.Full)]
    public void TryParseScope_AcceptsTheTwoKnownScopes(string input, JwtScope expected)
    {
        Assert.True(AuthCommand.TryParseScope(input, out var scope));
        Assert.Equal(expected, scope);
    }

    // A rejected scope must never fall through to Full. Before this validation existed,
    // every one of these minted a full-authority token.
    [Theory]
    [InlineData("metrcs")]
    [InlineData("Metrics")]
    [InlineData("read-only")]
    [InlineData("")]
    [InlineData(" metrics")]
    public void TryParseScope_RejectsAnythingElse(string input)
    {
        Assert.False(AuthCommand.TryParseScope(input, out _));
    }
}
