using System;
using System.IO;
using System.Linq;
using System.Text;
using Loom.DevTools.Commands;
using Loom.Security;
using Xunit;

namespace Loom.Telemetry.Tests;

public class AuthCommandTests
{
    // The three-way decision behind ResolveUsersFileForCli/ResolveKeyFileForCli. This is
    // the only way to exercise all three FileAccessState branches on Windows, where the
    // system default and dev-secrets are the same folder and the real fallback never
    // engages.
    [Fact]
    public void ResolveCliPath_EnvValueSet_WinsOutrightRegardlessOfState()
    {
        Assert.Equal("/env/path", AuthCommand.ResolveCliPath("/env/path", FileAccessState.Indeterminate, "/system/default", "/dev/secrets"));
    }

    [Fact]
    public void ResolveCliPath_NoEnvValue_DefaultExists_UsesSystemDefault()
    {
        Assert.Equal("/system/default", AuthCommand.ResolveCliPath(null, FileAccessState.Exists, "/system/default", "/dev/secrets"));
    }

    [Fact]
    public void ResolveCliPath_NoEnvValue_DefaultMissing_FallsBackToDevSecrets()
    {
        Assert.Equal("/dev/secrets", AuthCommand.ResolveCliPath(null, FileAccessState.Missing, "/system/default", "/dev/secrets"));
    }

    [Fact]
    public void ResolveCliPath_NoEnvValue_DefaultIndeterminate_RefusesRatherThanGuessing()
    {
        Assert.Null(AuthCommand.ResolveCliPath(null, FileAccessState.Indeterminate, "/system/default", "/dev/secrets"));
    }

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
    [InlineData("relative/cfg", "/home/u/.config/fish/config.fish")]
    public void ResolveUnixProfilePath_Fish_HonoursXdgConfigHome(string? xdgConfigHome, string expected)
    {
        Assert.Equal(expected, AuthCommand.ResolveUnixProfilePath("/usr/local/bin/fish", "/home/u", isMacOS: false, xdgConfigHome));
    }

    [Theory]
    [InlineData("/home/u/zdot", "/home/u/zdot/.zshrc")]
    [InlineData("/home/u/zdot/", "/home/u/zdot/.zshrc")]
    [InlineData(null, "/home/u/.zshrc")]
    [InlineData("", "/home/u/.zshrc")]
    [InlineData("relative/zdot", "/home/u/.zshrc")]
    public void ResolveUnixProfilePath_Zsh_HonoursZdotdir(string? zdotdir, string expected)
    {
        Assert.Equal(expected, AuthCommand.ResolveUnixProfilePath("/usr/bin/zsh", "/home/u", isMacOS: false, zdotdir: zdotdir));
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
    public void OutsideAssignmentWinsOverLoom_HandWrittenLineAfterExistingBlock_ReturnsTrue()
    {
        const string content = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE='/home/u/jwt.key'\n# <<< loom <<<\n" +
            "export LOOM_JWT_KEY_FILE=/my/own/key\n";

        Assert.True(AuthCommand.OutsideAssignmentWinsOverLoom(content, "LOOM_JWT_KEY_FILE"));
    }

    [Fact]
    public void OutsideAssignmentWinsOverLoom_HandWrittenLineBeforeExistingBlock_ReturnsFalse()
    {
        const string content = "export LOOM_JWT_KEY_FILE=/my/own/key\n" +
            "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE='/home/u/jwt.key'\n# <<< loom <<<\n";

        Assert.False(AuthCommand.OutsideAssignmentWinsOverLoom(content, "LOOM_JWT_KEY_FILE"));
    }

    // No existing block means loom's is appended at the very end of the file, so
    // nothing can come after it - the hand-written line always loses here regardless
    // of where it sits.
    [Fact]
    public void OutsideAssignmentWinsOverLoom_NoExistingBlock_ReturnsFalse()
    {
        const string content = "export LOOM_JWT_KEY_FILE=/my/own/key\n";

        Assert.False(AuthCommand.OutsideAssignmentWinsOverLoom(content, "LOOM_JWT_KEY_FILE"));
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

    // The exact regression this item fixes: a naive [^'"]* scan stopped at the FIRST
    // embedded quote, so this round-trip through the real writer used to come back
    // truncated as "/home/u/a" instead of the full path.
    [Fact]
    public void ExtractExistingUsersPath_RoundTripsPathContainingAnApostrophe()
    {
        var block = AuthCommand.RenderUnixPersistBlock("/bin/bash", "/home/u/jwt.key", "/home/u/a'b/users");

        Assert.Equal("/home/u/a'b/users", AuthCommand.ExtractExistingUsersPath(block));
    }

    [Fact]
    public void ExtractExistingUsersPath_SingleQuotedValueWithEscapedApostrophe_ReturnsFullValueNotTruncated()
    {
        const string content = "# >>> loom >>>\nexport LOOM_AUTH_USERS_FILE='/home/u/a'\\''b/users'\n# <<< loom <<<\n";

        Assert.Equal("/home/u/a'b/users", AuthCommand.ExtractExistingUsersPath(content));
    }

    [Fact]
    public void ExtractExistingUsersPath_DoubleQuotedValueWithEscapedQuote_ReturnsUnescapedValue()
    {
        const string content = "# >>> loom >>>\nexport LOOM_AUTH_USERS_FILE=\"/home/u/a\\\"b/users\"\n# <<< loom <<<\n";

        Assert.Equal("/home/u/a\"b/users", AuthCommand.ExtractExistingUsersPath(content));
    }

    // A hand-edited block or a pre-quoting version of loom writes this form - it must
    // not be dropped, which the carry-forward logic depends on.
    [Fact]
    public void ExtractExistingUsersPath_UnquotedValue_ReturnsValue()
    {
        const string content = "# >>> loom >>>\nexport LOOM_AUTH_USERS_FILE=/home/u/users\n# <<< loom <<<\n";

        Assert.Equal("/home/u/users", AuthCommand.ExtractExistingUsersPath(content));
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

    // The fish counterpart of ExtractExistingUsersPath_RoundTripsPathContainingAnApostrophe:
    // QuoteFishSingle writes \' for an embedded apostrophe, but ParseShellValue previously
    // understood only the POSIX '\''-splice form, so this value was misread and truncated
    // on the very next `loom auth init` run.
    [Fact]
    public void ExtractExistingUsersPath_Fish_RoundTripsValueWithApostropheDollarBacktickAndDoubleQuote()
    {
        const string weirdPath = "/home/u/a'b$c`d\"e/users";

        var block = AuthCommand.RenderUnixPersistBlock("/usr/local/bin/fish", "/home/u/jwt.key", weirdPath);

        Assert.Equal(weirdPath, AuthCommand.ExtractExistingUsersPath(block));
    }

    [Fact]
    public void ExtractExistingUsersPath_Posix_RoundTripsValueWithApostropheDollarBacktickAndDoubleQuote()
    {
        const string weirdPath = "/home/u/a'b$c`d\"e/users";

        var block = AuthCommand.RenderUnixPersistBlock("/bin/bash", "/home/u/jwt.key", weirdPath);

        Assert.Equal(weirdPath, AuthCommand.ExtractExistingUsersPath(block));
    }

    // POSIX single quotes have no escapes, so a literal backslash is written as one
    // backslash. ParseShellValue used to apply fish's \\ and \' unescaping to these too.
    [Theory]
    [InlineData("/bin/bash")]
    [InlineData("/usr/bin/zsh")]
    [InlineData("/usr/local/bin/fish")]
    public void ExtractExistingUsersPath_RoundTripsValueWithBackslashesApostropheDollarAndBacktick(string shellEnvValue)
    {
        const string weirdPath = "/home/u/a\\\\b\\'c'd$e`f/users";

        var block = AuthCommand.RenderUnixPersistBlock(shellEnvValue, "/home/u/jwt.key", weirdPath);

        Assert.Equal(weirdPath, AuthCommand.ExtractExistingUsersPath(block));
    }

    [UnixOnlyFact]
    public void ResolveProfileWriteTarget_DanglingSymlink_RefusesAndLeavesTheLinkAlone()
    {
        var dir = Directory.CreateTempSubdirectory("loom-profile-").FullName;
        try
        {
            var link = Path.Combine(dir, ".zshrc");
            var missingTarget = Path.Combine(dir, "dotfiles", "zshrc");
            File.CreateSymbolicLink(link, missingTarget);

            var result = AuthCommand.ResolveProfileWriteTarget(link, out var refusal);

            Assert.Null(result);
            Assert.Contains(link, refusal);
            Assert.Contains(missingTarget, refusal);
            Assert.Equal(missingTarget, new FileInfo(link).LinkTarget);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [UnixOnlyFact]
    public void ResolveProfileWriteTarget_DanglingSymlinkChain_Refuses()
    {
        var dir = Directory.CreateTempSubdirectory("loom-profile-").FullName;
        try
        {
            var missingTarget = Path.Combine(dir, "dotfiles", "zshrc");
            var middle = Path.Combine(dir, "middle");
            var link = Path.Combine(dir, ".zshrc");
            File.CreateSymbolicLink(middle, missingTarget);
            File.CreateSymbolicLink(link, middle);

            var result = AuthCommand.ResolveProfileWriteTarget(link, out var refusal);

            Assert.Null(result);
            Assert.Contains(link, refusal);
            Assert.Equal(middle, new FileInfo(link).LinkTarget);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [UnixOnlyFact]
    public void ResolveProfileWriteTarget_LiveSymlink_ResolvesToItsTarget()
    {
        var dir = Directory.CreateTempSubdirectory("loom-profile-").FullName;
        try
        {
            var target = Path.Combine(dir, "zshrc");
            File.WriteAllText(target, "# real\n");
            var link = Path.Combine(dir, ".zshrc");
            File.CreateSymbolicLink(link, target);

            var result = AuthCommand.ResolveProfileWriteTarget(link, out var refusal);

            Assert.Equal(target, result);
            Assert.Null(refusal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [UnixOnlyFact]
    public void ResolveProfileWriteTarget_RegularFile_ResolvesToItself()
    {
        var dir = Directory.CreateTempSubdirectory("loom-profile-").FullName;
        try
        {
            var file = Path.Combine(dir, ".zshrc");
            File.WriteAllText(file, "# real\n");

            var result = AuthCommand.ResolveProfileWriteTarget(file, out var refusal);

            Assert.Equal(file, result);
            Assert.Null(refusal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // The process umask (usually 022) strips group-write from UnixCreateMode, so a 664
    // profile used to come back 644. Proves nothing if the host's umask is 000.
    [UnixOnlyFact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void WriteProfileAtomically_PreservesTheExistingFilesPermissionBits()
    {
        var dir = Directory.CreateTempSubdirectory("loom-profile-").FullName;
        try
        {
            var file = Path.Combine(dir, ".zshrc");
            File.WriteAllText(file, "# old\n");
            const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead;
            File.SetUnixFileMode(file, mode);

            AuthCommand.WriteProfileAtomically(file, Encoding.UTF8.GetBytes("# new\n"));

            Assert.Equal("# new\n", File.ReadAllText(file));
            Assert.Equal(mode, File.GetUnixFileMode(file));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // KeyMaterial.DevSecretsDirectory is recomputed from XDG_DATA_HOME on every call, so a
    // read-only data home reaches EnsureDevSecretsDirectory's create. Before the fix this
    // threw an uncaught UnauthorizedAccessException out of Init.
    [UnixOnlyFact(RequireNonRoot = true)]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void Init_DataFolderNotWritable_ReturnsFalseAndCreatesNothing()
    {
        var dataHome = Directory.CreateTempSubdirectory("loom-xdg-").FullName;
        var previousXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var previousError = Console.Error;
        var previousOut = Console.Out;
        var error = new StringWriter();
        try
        {
            File.SetUnixFileMode(dataHome, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", dataHome);
            Console.SetError(error);
            Console.SetOut(TextWriter.Null);

            var succeeded = AuthCommand.Init(persist: false);

            Assert.False(succeeded);
            Assert.Contains("Could not create", error.ToString());
            Assert.Contains($"write to {dataHome}", error.ToString());
            Assert.Empty(Directory.GetFileSystemEntries(dataHome));
        }
        finally
        {
            Console.SetError(previousError);
            Console.SetOut(previousOut);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousXdg);
            File.SetUnixFileMode(dataHome, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(dataHome, recursive: true);
        }
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("a b")]
    [InlineData("José")]
    public void ValidateNewUsername_AcceptsOrdinaryNames(string name) =>
        Assert.Null(AuthCommand.ValidateNewUsername(name));

    [Fact]
    public void ValidateNewUsername_Accepts128BytesRejects129()
    {
        Assert.Null(AuthCommand.ValidateNewUsername(new string('a', UserStore.MaxUsernameBytes)));
        Assert.NotNull(AuthCommand.ValidateNewUsername(new string('a', UserStore.MaxUsernameBytes + 1)));
        // 43 x U+20AC is 43 chars but 129 bytes: bytes, not chars.
        Assert.NotNull(AuthCommand.ValidateNewUsername(new string('€', 43)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" alice")]
    [InlineData("alice ")]
    [InlineData("al:ice")]
    [InlineData(":alice")]
    [InlineData("#alice")]
    [InlineData("al\nice")]
    [InlineData("al\tice")]
    [InlineData("alice\r")]
    public void ValidateNewUsername_RefusesNamesTheHostWouldRejectOrMisread(string name) =>
        Assert.NotNull(AuthCommand.ValidateNewUsername(name));

    [Fact]
    public void UsernameExists_MatchesOrdinallyAndSkipsCommentsAndPaddedLines()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, ["# alice:x", "", $"  bob:{PasswordHasher.Hash("pw")}  ", $"Alice:{PasswordHasher.Hash("pw")}"]);

            Assert.True(AuthCommand.UsernameExists(path, "bob"));
            Assert.True(AuthCommand.UsernameExists(path, "Alice"));
            Assert.False(AuthCommand.UsernameExists(path, "alice"));   // ordinal, like Load
            Assert.False(AuthCommand.UsernameExists(path, "carol"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NearestExistingAncestor_SkipsDirectoriesThatDoNotExistYet()
    {
        var root = Directory.CreateTempSubdirectory("loom-anc-").FullName;
        try
        {
            var deep = Path.Combine(root, "missing", "Loom", "dev-secrets");
            Assert.Equal(root, AuthCommand.NearestExistingAncestor(deep));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private const string Note = "    (Command Prompt: this path contains '%' - use PowerShell, or set it in System Properties)";

    private static void AssertLines(string[] expected, string[] actual) => Assert.Equal(expected, actual);

    [Fact]
    public void RenderSetVarLines_Windows_HoldsBothLabelledForms()
    {
        var lines = AuthCommand.RenderSetVarLines(true, null,
            ("LOOM_JWT_KEY_FILE", @"C:\d\jwt.key"), ("LOOM_AUTH_USERS_FILE", @"C:\d\users"));

        AssertLines(
        [
            "  PowerShell:",
            @"    $env:LOOM_JWT_KEY_FILE = 'C:\d\jwt.key'",
            @"    $env:LOOM_AUTH_USERS_FILE = 'C:\d\users'",
            "  Command Prompt:",
            "    set \"LOOM_JWT_KEY_FILE=C:\\d\\jwt.key\"",
            "    set \"LOOM_AUTH_USERS_FILE=C:\\d\\users\"",
        ], lines);
    }

    [Fact]
    public void RenderSetVarLines_Windows_QuoteDoublesInPowerShellAndIsUntouchedForCmd()
    {
        var lines = AuthCommand.RenderSetVarLines(true, null, ("V", @"C:\o'brien\k"));

        Assert.Contains(@"    $env:V = 'C:\o''brien\k'", lines);
        Assert.Contains("    set \"V=C:\\o'brien\\k\"", lines);
    }

    [Fact]
    public void RenderSetVarLines_Windows_PercentGetsNoteInsteadOfCmdLine()
    {
        var lines = AuthCommand.RenderSetVarLines(true, null, ("V", @"C:\100%\k"));

        Assert.Contains(@"    $env:V = 'C:\100%\k'", lines);
        Assert.Contains(Note, lines);
        Assert.DoesNotContain(lines, l => l.Contains("set \"V="));
    }

    [Fact]
    public void RenderSetVarLines_Windows_DollarAndBacktickStayLiteralInBothForms()
    {
        var lines = AuthCommand.RenderSetVarLines(true, null, ("V", "C:\\a$b`c\\k"));

        Assert.Contains("    $env:V = 'C:\\a$b`c\\k'", lines);
        Assert.Contains("    set \"V=C:\\a$b`c\\k\"", lines);
    }

    [Fact]
    public void RenderSetVarLines_Unix_IsUnchangedAndUnlabelled()
    {
        var bash = AuthCommand.RenderSetVarLines(false, "/bin/bash", ("LOOM_JWT_KEY_FILE", "/h/it's/jwt.key"), ("LOOM_AUTH_USERS_FILE", "/h/users"));
        var fish = AuthCommand.RenderSetVarLines(false, "/usr/bin/fish", ("LOOM_JWT_KEY_FILE", "/h/it's/jwt.key"), ("LOOM_AUTH_USERS_FILE", "/h/users"));

        AssertLines(
        [
            "  export LOOM_JWT_KEY_FILE='/h/it'\\''s/jwt.key'",
            "  export LOOM_AUTH_USERS_FILE='/h/users'",
        ], bash);
        AssertLines(
        [
            "  set -gx LOOM_JWT_KEY_FILE '/h/it\\'s/jwt.key'",
            "  set -gx LOOM_AUTH_USERS_FILE '/h/users'",
        ], fish);
    }

    [Fact]
    public void RenderAddUserHint_Windows_HasBothForms_AndPercentNote()
    {
        var hint = AuthCommand.RenderAddUserHint(true, null, @"C:\o'b\users");
        Assert.Equal("Then add an operator:", hint[0]);
        Assert.Equal(@"  PowerShell:      loom auth add-user operator --users-file 'C:\o''b\users'", hint[1]);
        Assert.Equal("  Command Prompt:  loom auth add-user operator --users-file \"C:\\o'b\\users\"", hint[2]);

        var pct = AuthCommand.RenderAddUserHint(true, null, @"C:\100%\users");
        Assert.Equal("  Command Prompt:  " + Note.TrimStart(), pct[2]);
    }

    [Fact]
    public void RenderAddUserHint_Unix_IsOneUnlabelledLine()
    {
        AssertLines(["Then add an operator:  loom auth add-user operator --users-file '/h/users'"],
            AuthCommand.RenderAddUserHint(false, "/bin/bash", "/h/users"));
        AssertLines(["Then add an operator:  loom auth add-user operator --users-file '/h/it\\'s'"],
            AuthCommand.RenderAddUserHint(false, "/usr/bin/fish", "/h/it's"));
    }

    [Fact]
    public void UpsertUnixPersistBlockBytes_NonAsciiPath_EncodesBlockAsUtf8NotLatin1()
    {
        var block = AuthCommand.RenderUnixPersistBlock("/bin/bash", "/home/andré/jwt.key", null);

        var result = AuthCommand.UpsertUnixPersistBlockBytes([], "", block);

        Assert.Equal(Encoding.UTF8.GetBytes(block), result);
        Assert.NotEqual(Encoding.Latin1.GetBytes(block), result);
    }

    [Fact]
    public void UpsertUnixPersistBlockBytes_ExistingBytes_ArePreservedVerbatim()
    {
        // A byte that is not valid UTF-8 standing alone (0xE9, the Latin1 encoding of
        // 'é' a hand-edited .bashrc might carry) must survive untouched - proving the
        // splice never round-trips existingBytes through any encoding, only the new
        // block.
        var existingBytes = new byte[] { (byte)'#', (byte)' ', 0xE9, (byte)'\n' };
        var existingLatin1 = Encoding.Latin1.GetString(existingBytes);
        const string block = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE='/home/u/jwt.key'\n# <<< loom <<<\n";

        var result = AuthCommand.UpsertUnixPersistBlockBytes(existingBytes, existingLatin1, block);

        Assert.Equal(existingBytes, result[..4]);
        Assert.Equal(Encoding.UTF8.GetBytes(block), result[4..]);
    }

    // The item 2 regression: ExtractExistingUsersPath's caller works against the Latin-1
    // decoding of the profile's raw bytes (so match indices line up with byte offsets),
    // but a UTF-8-encoded path with a non-ASCII segment then comes back garbled unless the
    // extracted value is re-decoded as UTF-8 before use. This drives the exact pipeline
    // PersistEnvironmentVariablesUnix uses - render as UTF-8 bytes, decode as Latin-1
    // (what "existing" is in that method), extract, then recover via the fix - for both a
    // Latin script accent and a CJK segment.
    [Theory]
    [InlineData("/home/andré/dev-secrets/users")]
    [InlineData("/home/用户/dev-secrets/users")]
    public void ExtractExistingUsersPath_CarryForwardOfNonAsciiPath_SurvivesLatin1DecodeUtf8RecodeRoundTrip(string originalPath)
    {
        var block = AuthCommand.RenderUnixPersistBlock("/bin/bash", "/home/u/jwt.key", originalPath);
        var blockBytes = Encoding.UTF8.GetBytes(block);
        var latin1Decoded = Encoding.Latin1.GetString(blockBytes);

        var extracted = AuthCommand.ExtractExistingUsersPath(latin1Decoded);
        var recovered = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(extracted!));

        Assert.Equal(originalPath, recovered);
    }

    [Fact]
    public void UnixPersistMarkersArePaired_NoMarkers_ReturnsTrue()
    {
        Assert.True(AuthCommand.UnixPersistMarkersArePaired("export EDITOR=vim\n"));
    }

    [Fact]
    public void UnixPersistMarkersArePaired_WellFormedSingleBlock_ReturnsTrue()
    {
        const string content = "# before\n# >>> loom >>>\nexport LOOM_JWT_KEY_FILE='/home/u/jwt.key'\n# <<< loom <<<\n# after\n";

        Assert.True(AuthCommand.UnixPersistMarkersArePaired(content));
    }

    [Fact]
    public void UnixPersistMarkersArePaired_WellFormedMultipleBlocks_ReturnsTrue()
    {
        const string content =
            "# before\n" +
            "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"a\"\n# <<< loom <<<\n" +
            "# middle\n" +
            "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"b\"\n# <<< loom <<<\n" +
            "# after\n";

        Assert.True(AuthCommand.UnixPersistMarkersArePaired(content));
    }

    [Fact]
    public void UnixPersistMarkersArePaired_OrphanedOpeningMarker_ReturnsFalse()
    {
        const string content = "# >>> loom >>>\nalias ll='ls -l'\nexport PATH=/usr/local/bin:$PATH\n";

        Assert.False(AuthCommand.UnixPersistMarkersArePaired(content));
    }

    [Fact]
    public void UnixPersistMarkersArePaired_OrphanedClosingMarker_ReturnsFalse()
    {
        const string content = "alias ll='ls -l'\n# <<< loom <<<\n";

        Assert.False(AuthCommand.UnixPersistMarkersArePaired(content));
    }

    [Fact]
    public void UnixPersistMarkersArePaired_TwoOpeningsThenOneClosing_ReturnsFalse()
    {
        const string content = "# >>> loom >>>\n# >>> loom >>>\nexport LOOM_JWT_KEY_FILE=\"a\"\n# <<< loom <<<\n";

        Assert.False(AuthCommand.UnixPersistMarkersArePaired(content));
    }

    // The exact 172-character scenario from PROMPT-auth-persist-round7.md: an orphaned
    // opening marker (the closing marker hand-deleted), then a user's alias and PATH
    // lines, then a complete, well-formed block. Before this validator, every one of the
    // five block-matching functions would match from the orphaned opener all the way to
    // the complete block's closer, swallowing the alias and PATH lines. Asserting the
    // validator rejects this is what keeps that from ever being reachable again.
    [Fact]
    public void UnixPersistMarkersArePaired_OrphanedOpenerFollowedByCompleteBlock_ReturnsFalse()
    {
        const string content =
            "# >>> loom >>>\n" +
            "alias ll='ls -l'\n" +
            "export PATH=/usr/local/bin:$PATH\n" +
            "# >>> loom >>>\n" +
            "export LOOM_JWT_KEY_FILE='/home/u/jwt.key'\n" +
            "# <<< loom <<<\n";

        Assert.False(AuthCommand.UnixPersistMarkersArePaired(content));
    }

    [Fact]
    public void UpsertUnixPersistBlockBytes_ReplacesExistingBlockAndPreservesSurroundingBytesVerbatim()
    {
        var before = Encoding.Latin1.GetBytes("# before é\n");
        var oldBlock = "# >>> loom >>>\nexport LOOM_JWT_KEY_FILE='/home/andré/old.key'\n# <<< loom <<<\n";
        var oldBlockBytes = Encoding.UTF8.GetBytes(oldBlock);
        var after = Encoding.Latin1.GetBytes("# after\n");
        var existingBytes = before.Concat(oldBlockBytes).Concat(after).ToArray();
        var existingLatin1 = Encoding.Latin1.GetString(existingBytes);

        var newBlock = AuthCommand.RenderUnixPersistBlock("/bin/bash", "/home/andré/new.key", null);

        var result = AuthCommand.UpsertUnixPersistBlockBytes(existingBytes, existingLatin1, newBlock);

        var expected = before.Concat(Encoding.UTF8.GetBytes(newBlock)).Concat(after).ToArray();
        Assert.Equal(expected, result);
    }
}
