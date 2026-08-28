namespace Loom.Security;

public static class KeyMaterial
{
    public const string KeyFileVariable = "LOOM_JWT_KEY_FILE";
    public const string UsersFileVariable = "LOOM_AUTH_USERS_FILE";
    public const string DefaultKeyFile = "/var/secrets/loom/jwt.key";
    public const string DefaultUsersFile = "/var/secrets/loom/users";
    private const int MinimumKeyBytes = 32;

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
