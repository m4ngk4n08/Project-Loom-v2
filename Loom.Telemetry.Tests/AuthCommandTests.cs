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

    [Theory]
    [InlineData("/usr/bin/zsh", "/home/u/.zshrc")]
    [InlineData("/bin/bash", "/home/u/.bashrc")]
    [InlineData("/usr/local/bin/fish", "/home/u/.config/fish/config.fish")]
    [InlineData("/bin/tcsh", "/home/u/.profile")]
    [InlineData(null, "/home/u/.profile")]
    [InlineData("", "/home/u/.profile")]
    public void ResolveUnixProfilePath_Linux_MapsShellBasenameToExpectedFile(string? shellEnvValue, string expected)
    {
        Assert.Equal(expected, AuthCommand.ResolveUnixProfilePath(shellEnvValue, "/home/u", isMacOS: false));
    }

    // isMacOS is now an explicit parameter rather than an OperatingSystem.IsMacOS()
    // read inside ResolveUnixProfilePath, so both branches of the bash mapping are
    // testable here on Windows CI with no Mac required.
    [Fact]
    public void ResolveUnixProfilePath_MacOsBash_UsesBashProfileNotBashrc()
    {
        Assert.Equal("/home/u/.bash_profile", AuthCommand.ResolveUnixProfilePath("/bin/bash", "/home/u", isMacOS: true));
    }

    [Fact]
    public void ResolveUnixProfilePath_LinuxBash_UsesBashrcNotBashProfile()
    {
        Assert.Equal("/home/u/.bashrc", AuthCommand.ResolveUnixProfilePath("/bin/bash", "/home/u", isMacOS: false));
    }

    // Non-bash shells are unaffected by isMacOS - covers both values so a future
    // regression that starts branching on it for zsh/fish is caught here.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveUnixProfilePath_NonBashShells_UnaffectedByIsMacOs(bool isMacOS)
    {
        Assert.Equal("/home/u/.zshrc", AuthCommand.ResolveUnixProfilePath("/usr/bin/zsh", "/home/u", isMacOS));
    }

    [Theory]
    [InlineData(null, "/home/u/.config/fish/config.fish")]
    [InlineData("", "/home/u/.config/fish/config.fish")]
    [InlineData("/home/u/xdgcfg", "/home/u/xdgcfg/fish/config.fish")]
    [InlineData("/home/u/xdgcfg/", "/home/u/xdgcfg/fish/config.fish")]
    public void ResolveUnixProfilePath_Fish_HonoursXdgConfigHome(string? xdgConfigHome, string expected)
    {
        Assert.Equal(expected, AuthCommand.ResolveUnixProfilePath("/usr/local/bin/fish", "/home/u", isMacOS: false, xdgConfigHome));
    }

    [Fact]
    public void RenderUnixPersistBlock_Fish_UsesSetDashGx()
    {
        var block = AuthCommand.RenderUnixPersistBlock("/usr/local/bin/fish", "/home/u/.local/share/Loom/dev-secrets/jwt.key", "/home/u/.local/share/Loom/dev-secrets/users");

        Assert.Equal(
            "# >>> loom >>>\n" +
            "set -gx LOOM_JWT_KEY_FILE '/home/u/.local/share/Loom/dev-secrets/jwt.key'\n" +
            "set -gx LOOM_AUTH_USERS_FILE '/home/u/.local/share/Loom/dev-secrets/users'\n" +
            "# <<< loom <<<\n",
            block);
    }

    [Theory]
    [InlineData("/bin/bash")]
    [InlineData("/usr/bin/zsh")]
    public void RenderUnixPersistBlock_BashAndZsh_UseExport(string shellEnvValue)
    {
        var block = AuthCommand.RenderUnixPersistBlock(shellEnvValue, "/home/u/jwt.key", "/home/u/users");

        Assert.Equal(
            "# >>> loom >>>\n" +
            "export LOOM_JWT_KEY_FILE='/home/u/jwt.key'\n" +
            "export LOOM_AUTH_USERS_FILE='/home/u/users'\n" +
            "# <<< loom <<<\n",
            block);
    }

    [Fact]
    public void RenderUnixPersistBlock_ValueWithShellMetacharacters_IsSingleQuotedAndEscaped()
    {
        const string weirdPath = "/home/u/a`b$c'd/jwt.key";

        var bashBlock = AuthCommand.RenderUnixPersistBlock("/bin/bash", weirdPath, null);
        Assert.Equal(
            "# >>> loom >>>\n" +
            "export LOOM_JWT_KEY_FILE='/home/u/a`b$c'\\''d/jwt.key'\n" +
            "# <<< loom <<<\n",
            bashBlock);

        var fishBlock = AuthCommand.RenderUnixPersistBlock("/usr/local/bin/fish", weirdPath, null);
        Assert.Equal(
            "# >>> loom >>>\n" +
            "set -gx LOOM_JWT_KEY_FILE '/home/u/a`b$c\\'d/jwt.key'\n" +
            "# <<< loom <<<\n",
            fishBlock);
    }

    [Fact]
    public void FindVariablesAssignedOutsideBlock_HandWrittenExportOutsideBlock_IsDetected()
    {
        const string content = "export LOOM_JWT_KEY_FILE=/my/own/key\n" +
            "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE='/home/u/jwt.key'\n# <<< loom <<<\n";

        var found = AuthCommand.FindVariablesAssignedOutsideBlock(content);

        Assert.Contains("LOOM_JWT_KEY_FILE", found);
    }

    [Fact]
    public void FindVariablesAssignedOutsideBlock_OnlyAssignedInsideBlock_IsNotDetected()
    {
        const string content = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE='/home/u/jwt.key'\n# <<< loom <<<\n";

        var found = AuthCommand.FindVariablesAssignedOutsideBlock(content);

        Assert.Empty(found);
    }

    [Fact]
    public void FindVariablesAssignedOutsideBlock_NoAssignmentAnywhere_IsNotDetected()
    {
        var found = AuthCommand.FindVariablesAssignedOutsideBlock("# my custom profile\nexport EDITOR=vim\n");

        Assert.Empty(found);
    }

    [Fact]
    public void UpsertUnixPersistBlock_NoExistingBlock_AppendsAndKeepsOriginalContent()
    {
        const string existing = "# my custom profile\nexport EDITOR=vim\n";
        const string block = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"x\"\n# <<< loom <<<\n";

        var result = AuthCommand.UpsertUnixPersistBlock(existing, block);

        Assert.Equal(existing + block, result);
    }

    [Fact]
    public void UpsertUnixPersistBlock_NoExistingBlock_EmptyContent_JustReturnsBlock()
    {
        const string block = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"x\"\n# <<< loom <<<\n";

        var result = AuthCommand.UpsertUnixPersistBlock(string.Empty, block);

        Assert.Equal(block, result);
    }

    [Fact]
    public void UpsertUnixPersistBlock_ExistingBlock_ReplacesInPlaceWithoutDuplicating()
    {
        const string before = "# before\n";
        const string oldBlock = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"old\"\n# <<< loom <<<\n";
        const string after = "# after\n";
        const string newBlock = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"new\"\n# <<< loom <<<\n";

        var result = AuthCommand.UpsertUnixPersistBlock(before + oldBlock + after, newBlock);

        Assert.Equal(before + newBlock + after, result);
    }

    [Fact]
    public void UpsertUnixPersistBlock_BlockAtEndOfFileWithNoTrailingNewline_IsReplaced()
    {
        const string before = "# before\n";
        const string oldBlockNoTrailingNewline = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"old\"\n# <<< loom <<<";
        const string newBlock = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"new\"\n# <<< loom <<<\n";

        var result = AuthCommand.UpsertUnixPersistBlock(before + oldBlockNoTrailingNewline, newBlock);

        Assert.Equal(before + newBlock, result);
    }

    [Fact]
    public void ExtractExistingUsersPath_BlockPresentWithExportSyntax_ReturnsValue()
    {
        const string content = "# before\n# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"k\"\nexport LOOM_AUTH_USERS_FILE=\"u\"\n# <<< loom <<<\n";

        Assert.Equal("u", AuthCommand.ExtractExistingUsersPath(content));
    }

    [Fact]
    public void ExtractExistingUsersPath_BlockPresentWithFishSyntax_ReturnsValue()
    {
        const string content = "# >>> loom >>>\nset -gx LOOM_JWT_KEY_FILE \"k\"\nset -gx LOOM_AUTH_USERS_FILE \"u\"\n# <<< loom <<<\n";

        Assert.Equal("u", AuthCommand.ExtractExistingUsersPath(content));
    }

    [Fact]
    public void ExtractExistingUsersPath_BlockPresentWithNoUsersLine_ReturnsNull()
    {
        const string content = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"k\"\n# <<< loom <<<\n";

        Assert.Null(AuthCommand.ExtractExistingUsersPath(content));
    }

    [Fact]
    public void ExtractExistingUsersPath_NoBlock_ReturnsNull()
    {
        Assert.Null(AuthCommand.ExtractExistingUsersPath("export EDITOR=vim\n"));
    }

    [Fact]
    public void UpsertUnixPersistBlock_TwoExistingBlocks_CollapsesToOneAtFirstPosition()
    {
        const string before = "# before\n";
        const string oldBlockA = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"a\"\n# <<< loom <<<\n";
        const string middle = "# middle\n";
        const string oldBlockB = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"b\"\n# <<< loom <<<\n";
        const string after = "# after\n";
        const string newBlock = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"new\"\n# <<< loom <<<\n";

        var result = AuthCommand.UpsertUnixPersistBlock(before + oldBlockA + middle + oldBlockB + after, newBlock);

        Assert.Equal(before + newBlock + middle + after, result);
    }
}
