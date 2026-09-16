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
}
