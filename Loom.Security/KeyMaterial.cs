namespace Loom.Security;

public static class KeyMaterial
{
    public const string KeyFileVariable = "LOOM_JWT_KEY_FILE";
    public const string UsersFileVariable = "LOOM_AUTH_USERS_FILE";
    private const int MinimumKeyBytes = 32;

    /// <summary>The one definition of where dev-secrets live. `loom auth init` writes
    /// here. On Unix this is NOT where the host looks by default - see
    /// SystemDefaultDirectory; setup and lookup there are connected only by the
    /// environment variables, which `--persist` sets, and never silently coincide. On
    /// Windows this folder IS the host's default (see SystemDefaultDirectory) because
    /// there is no separate production deployment to protect from it.</summary>
    public static string DevSecretsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loom", "dev-secrets");

    /// <summary>Where the host looks when the env vars are unset. On Unix this is
    /// system-scoped and not user-writable, so an ephemeral dev key cannot reach
    /// production as a silent fallback - Loom has a documented Linux production
    /// deployment (systemd unit, `loomd` service user, this path at mode 400). On
    /// Windows there is no production deployment at all - no service unit, no
    /// installer, no hardening guide - so the default is deliberately the developer's
    /// own per-user folder. %ProgramData% was considered and rejected: `icacls
    /// C:\ProgramData` grants BUILTIN\Users (WD,AD) - any standard user can create and
    /// own a subdirectory there, including one named to match this path, before Loom
    /// ever runs. LoadSigningKey validates only base64 and length, never ownership, so
    /// that would let an unprivileged user plant the signing key a real deployment
    /// trusts by default - worse than the per-user default it would replace.</summary>
    private static string SystemDefaultDirectory =>
        OperatingSystem.IsWindows() ? DevSecretsDirectory : "/var/secrets/loom";

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
        switch (FileAccessCheck.Check(path))
        {
            case FileAccessState.Missing:
                throw new InvalidOperationException(
                    $"Loom auth: signing key not found at '{path}'. Set {KeyFileVariable} or run 'loom auth init'.");
            case FileAccessState.Indeterminate:
                throw new InvalidOperationException(
                    $"Loom auth: cannot access '{path}' - this process cannot read it. Check permissions on the file and its directory.");
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                $"Loom auth: '{path}' exists but this process cannot read it. Check its ownership and permissions.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(text.Trim());
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
