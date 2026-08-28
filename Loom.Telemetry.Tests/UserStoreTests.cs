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

    private static string WriteUsersFile(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, lines);
        return path;
    }
}
