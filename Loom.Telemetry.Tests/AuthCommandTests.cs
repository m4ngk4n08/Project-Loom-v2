using Loom.DevTools.Commands;
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
}
