using System;
using System.IO;
using Loom.Security;
using Xunit;

namespace Loom.Telemetry.Tests;

// Indeterminate (UnauthorizedAccessException, e.g. from a directory this process cannot
// traverse) is deliberately not covered here - producing it requires actually denying
// this process access via OS permissions (icacls on Windows, chmod on Unix), which is not
// a portable unit test across the CI matrix. It is covered instead by a runtime probe
// (see PROMPT-auth-persist-round6.md's required probes) that denies directory traversal
// with icacls and asserts the resulting message and exit code.
public class FileAccessCheckTests
{
    [Fact]
    public void Check_ExistingFile_ReturnsExists()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Equal(FileAccessState.Exists, FileAccessCheck.Check(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Check_MissingFile_ReturnsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.False(File.Exists(path));

        Assert.Equal(FileAccessState.Missing, FileAccessCheck.Check(path));
    }

    [Fact]
    public void Check_MissingParentDirectory_ReturnsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nested", "file");

        Assert.Equal(FileAccessState.Missing, FileAccessCheck.Check(path));
    }

    // The most important test in this round: proves Check never throws for any path
    // shape, over the whole category of inputs that have caused a crash in a previous
    // round (empty, whitespace, embedded NUL - all ArgumentException from File.OpenRead)
    // plus every other shape named in PROMPT-auth-persist-round7.md's table. The
    // directory case is asserted as Indeterminate deliberately - see the comment below.
    [Fact]
    public void Check_NeverThrows_ForAnyPathShape()
    {
        var tempDir = Directory.CreateTempSubdirectory("loom-fac-");
        try
        {
            var existingFile = Path.Combine(tempDir.FullName, "exists.txt");
            File.WriteAllText(existingFile, "x");

            var missingUnderExistingDir = Path.Combine(tempDir.FullName, "missing.txt");
            var missingUnderMissingDir = Path.Combine(tempDir.FullName, "no-such-dir", "file.txt");

            // Deliberate: on both Windows and Linux, opening a directory as a file throws
            // UnauthorizedAccessException, which Check already maps to Indeterminate.
            // Detecting the directory case cheaply (Directory.Exists before the open)
            // would let the message stop saying "check permissions" for a plain
            // pointed-at-a-folder mistake, but it also adds a TOCTOU window (the path
            // could stop being a directory between the check and the open) for a
            // cosmetic message improvement on a path that is not on any hot path. Left
            // as Indeterminate; not worth the extra syscall and race window.
            var directoryAsFile = tempDir.FullName;

            var cases = new (string label, string path, FileAccessState expected)[]
            {
                ("empty string", "", FileAccessState.Missing),
                ("whitespace only", "   ", FileAccessState.Missing),
                ("NUL character inside the path", "a\0b", FileAccessState.Missing),
                ("relative path to nothing", Guid.NewGuid().ToString("N") + "-loom-missing.txt", FileAccessState.Missing),
                ("absolute path to nothing in an existing directory", missingUnderExistingDir, FileAccessState.Missing),
                ("path under a directory that does not exist", missingUnderMissingDir, FileAccessState.Missing),
                ("an existing readable file", existingFile, FileAccessState.Exists),
                ("a directory where a file is expected", directoryAsFile, FileAccessState.Indeterminate),
            };

            foreach (var (label, path, expected) in cases)
            {
                FileAccessState actual;
                try
                {
                    actual = FileAccessCheck.Check(path);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"{label}: Check threw {ex.GetType().Name} instead of returning a state.");
                    return;
                }
                Assert.True(expected == actual, $"{label}: expected {expected}, got {actual}.");
            }

            // Dangling symlink - only where the OS lets this test create one (Developer
            // Mode or elevation on Windows; unprivileged on Linux/macOS). If creation
            // fails, this case does not run - it is neither asserted nor silently passed.
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
                Assert.Equal(FileAccessState.Missing, FileAccessCheck.Check(linkPath));
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
