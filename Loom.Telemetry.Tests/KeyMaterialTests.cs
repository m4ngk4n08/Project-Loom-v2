using System;
using System.IO;
using Loom.Security;
using Xunit;

namespace Loom.Telemetry.Tests;

// The runtime case this guards - GetFolderPath(LocalApplicationData) returning "" for an
// account with no profile folder, which makes DefaultKeyFile/DefaultUsersFile relative -
// cannot be produced on this machine, so IsUsableDefaultPath is tested here as a pure
// function with an injected path instead.
public class KeyMaterialTests
{
    [Theory]
    [InlineData(@"C:\Users\u\AppData\Local\Loom\dev-secrets\jwt.key")]
    [InlineData("/var/secrets/loom/jwt.key")]
    public void IsUsableDefaultPath_RootedPath_ReturnsTrue(string path)
    {
        Assert.True(KeyMaterial.IsUsableDefaultPath(path));
    }

    [Theory]
    [InlineData(@"Loom\dev-secrets\jwt.key")]
    [InlineData("")]
    public void IsUsableDefaultPath_RelativePath_ReturnsFalse(string path)
    {
        Assert.False(KeyMaterial.IsUsableDefaultPath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void IsEnvironmentValueSet_NullEmptyOrWhitespace_ReturnsFalse(string? value)
    {
        Assert.False(KeyMaterial.IsEnvironmentValueSet(value));
    }

    [Fact]
    public void IsEnvironmentValueSet_RealPath_ReturnsTrue()
    {
        Assert.True(KeyMaterial.IsEnvironmentValueSet("/var/secrets/loom/jwt.key"));
    }

    // The host's fail-closed contract, stated as a test: over the whole category of
    // malformed-path inputs from PROMPT-auth-persist-round7.md's table, LoadSigningKey
    // must never throw anything except InvalidOperationException - the one exception type
    // every caller (the dashboard host, `loom auth token`) already catches.
    [Fact]
    public void LoadSigningKey_NeverThrowsExceptInvalidOperationException_ForAnyPathShape()
    {
        var tempDir = Directory.CreateTempSubdirectory("loom-key-");
        try
        {
            var existingFile = Path.Combine(tempDir.FullName, "exists.txt");
            File.WriteAllText(existingFile, "not-valid-base64!!");

            var paths = new[]
            {
                "",
                "   ",
                "a\0b",
                Guid.NewGuid().ToString("N") + "-loom-missing.key",
                Path.Combine(tempDir.FullName, "missing.key"),
                Path.Combine(tempDir.FullName, "no-such-dir", "file.key"),
                existingFile,
                tempDir.FullName, // a directory where a file is expected
            };

            foreach (var path in paths)
            {
                try
                {
                    KeyMaterial.LoadSigningKey(path);
                }
                catch (InvalidOperationException)
                {
                    // expected - the fail-closed contract
                }
                catch (Exception ex)
                {
                    Assert.Fail($"LoadSigningKey('{path.Replace('\0', '?')}') threw {ex.GetType().Name}, not InvalidOperationException.");
                }
            }

            // Dangling symlink - only where the OS lets this test create one. Not run,
            // not asserted, if it cannot.
            var linkPath = Path.Combine(tempDir.FullName, "dangling-link");
            var linkTarget = Path.Combine(tempDir.FullName, "link-target-does-not-exist");
            try
            {
                File.CreateSymbolicLink(linkPath, linkTarget);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            try
            {
                try
                {
                    KeyMaterial.LoadSigningKey(linkPath);
                }
                catch (InvalidOperationException)
                {
                    // expected
                }
            }
            finally
            {
                File.Delete(linkPath);
            }
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
