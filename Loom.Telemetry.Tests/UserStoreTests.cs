using System;
using System.IO;
using Loom.Security;
using Xunit;

namespace Loom.Telemetry.Tests;

public class UserStoreTests
{
    [Fact]
    public void ValidFile_CorrectPassword_VerifyReturnsTrue()
    {
        var path = WriteUsersFile($"alice:{PasswordHasher.Hash("s3cret")}");
        try
        {
            var store = UserStore.Load(path);
            Assert.True(store.Verify("alice", "s3cret"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ValidFile_WrongPassword_VerifyReturnsFalse()
    {
        var path = WriteUsersFile($"alice:{PasswordHasher.Hash("s3cret")}");
        try
        {
            var store = UserStore.Load(path);
            Assert.False(store.Verify("alice", "wrong"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UnknownUsername_VerifyReturnsFalse()
    {
        var path = WriteUsersFile($"alice:{PasswordHasher.Hash("s3cret")}");
        try
        {
            var store = UserStore.Load(path);
            Assert.False(store.Verify("bob", "s3cret"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CommentsAndBlankLines_AreIgnored()
    {
        var path = WriteUsersFile(
            "# a comment",
            "",
            $"alice:{PasswordHasher.Hash("s3cret")}",
            "   ");
        try
        {
            var store = UserStore.Load(path);
            Assert.True(store.Verify("alice", "s3cret"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LineWithNoColon_ThrowsNamingLineNumber()
    {
        var path = WriteUsersFile($"alice:{PasswordHasher.Hash("s3cret")}", "no-colon-here");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => UserStore.Load(path));
            Assert.Contains(":2", ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LineWithUnparseableHash_Throws()
    {
        var path = WriteUsersFile("alice:not-a-real-hash");
        try
        {
            Assert.Throws<InvalidOperationException>(() => UserStore.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DuplicateUsername_Throws()
    {
        var path = WriteUsersFile(
            $"alice:{PasswordHasher.Hash("s3cret")}",
            $"alice:{PasswordHasher.Hash("other")}");
        try
        {
            Assert.Throws<InvalidOperationException>(() => UserStore.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EmptyOrCommentsOnlyFile_Throws()
    {
        var path = WriteUsersFile("# nothing here", "");
        try
        {
            Assert.Throws<InvalidOperationException>(() => UserStore.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingFile_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.False(File.Exists(path));
        Assert.Throws<InvalidOperationException>(() => UserStore.Load(path));
    }

    // The host's fail-closed contract, stated as a test: over the whole category of
    // malformed-path inputs from PROMPT-auth-persist-round7.md's table, Load must never
    // throw anything except InvalidOperationException.
    [Fact]
    public void Load_NeverThrowsExceptInvalidOperationException_ForAnyPathShape()
    {
        var tempDir = Directory.CreateTempSubdirectory("loom-users-");
        try
        {
            var existingFile = Path.Combine(tempDir.FullName, "exists.txt");
            File.WriteAllText(existingFile, "not-a-users-file");

            var paths = new[]
            {
                "",
                "   ",
                "a\0b",
                Guid.NewGuid().ToString("N") + "-loom-missing-users",
                Path.Combine(tempDir.FullName, "missing-users"),
                Path.Combine(tempDir.FullName, "no-such-dir", "users"),
                existingFile,
                tempDir.FullName, // a directory where a file is expected
            };

            foreach (var path in paths)
            {
                try
                {
                    UserStore.Load(path);
                }
                catch (InvalidOperationException)
                {
                    // expected - the fail-closed contract
                }
                catch (Exception ex)
                {
                    Assert.Fail($"UserStore.Load('{path.Replace('\0', '?')}') threw {ex.GetType().Name}, not InvalidOperationException.");
                }
            }

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
                    UserStore.Load(linkPath);
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

    private static string WriteUsersFile(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, lines);
        return path;
    }
}
