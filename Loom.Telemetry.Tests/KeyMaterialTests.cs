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
}
