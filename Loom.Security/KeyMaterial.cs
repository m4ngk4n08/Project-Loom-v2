namespace Loom.Security;

public static class KeyMaterial
{
    public const string KeyFileVariable = "LOOM_JWT_KEY_FILE";
    public const string UsersFileVariable = "LOOM_AUTH_USERS_FILE";
    private const int MinimumKeyBytes = 32;

    /// <summary>The one definition of where dev-secrets live. `loom auth init` writes
    /// here. This is NOT where the host looks by default - see SystemDefaultDirectory.
    /// Setup and lookup are connected by the environment variables, which `--persist`
    /// sets; they never silently coincide.</summary>
    public static string DevSecretsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loom", "dev-secrets");

    /// <summary>Where the host looks when the env vars are unset - system-scoped, not
    /// user-writable, so an ephemeral dev key cannot reach production as a silent
    /// fallback. Do not use SpecialFolder.CommonApplicationData on Unix: .NET maps it to
    /// /usr/share there, which is not a secrets location.</summary>
    private static string SystemDefaultDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Loom", "secrets")
        : "/var/secrets/loom";

    public static string DefaultKeyFile => Path.Combine(SystemDefaultDirectory, "jwt.key");

    public static string DefaultUsersFile => Path.Combine(SystemDefaultDirectory, "users");

    public static string ResolveKeyFile() =>
        Environment.GetEnvironmentVariable(KeyFileVariable) ?? DefaultKeyFile;

    public static string ResolveUsersFile() =>
        Environment.GetEnvironmentVariable(UsersFileVariable) ?? DefaultUsersFile;

    /// <summary>Fail closed. There is no generated-on-the-fly fallback in any
    /// environment - an ephemeral dev key is precisely the convenience that reaches
    /// production by accident.</summary>
    public static byte[] LoadSigningKey(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Loom auth: signing key not found at '{path}'. Set {KeyFileVariable} or run 'loom auth init'.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(File.ReadAllText(path).Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"Loom auth: '{path}' is not valid base64.");
        }

        if (key.Length < MinimumKeyBytes)
            throw new InvalidOperationException(
                $"Loom auth: '{path}' decodes to {key.Length} bytes; at least {MinimumKeyBytes} are required.");

        return key;
    }
}
