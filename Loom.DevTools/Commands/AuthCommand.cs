using Loom.Security;
using System.Security.Cryptography;
using System.Text;

namespace Loom.DevTools.Commands;

/// <summary>loom auth init | add-user &lt;name&gt; | hash | token --sub X [--scope metrics] [--ttl 90d]</summary>
public static class AuthCommand
{
    private static string DevSecretsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loom", "dev-secrets");

    public static void Init(bool persist = false)
    {
        Directory.CreateDirectory(DevSecretsDirectory);
        var keyPath = Path.Combine(DevSecretsDirectory, "jwt.key");
        var usersPath = Path.Combine(DevSecretsDirectory, "users");

        if (File.Exists(keyPath))
        {
            Console.WriteLine($"Refusing to overwrite an existing signing key at {keyPath}.");
            Console.WriteLine("Delete it deliberately if you intend to rotate - every outstanding token dies with it.");
            Console.WriteLine("(--persist is also skipped - nothing was written or changed.)");
            return;
        }

        File.WriteAllText(keyPath, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        if (!File.Exists(usersPath)) File.WriteAllText(usersPath, "# username:pbkdf2-sha256$...\n");

        Console.WriteLine($"Wrote {keyPath}");
        Console.WriteLine($"Wrote {usersPath}");
        Console.WriteLine();
        Console.WriteLine("Set these before starting Loom.Web.Api or loom-dashboard:");
        Console.WriteLine($"  $env:{KeyMaterial.KeyFileVariable} = \"{keyPath}\"");
        Console.WriteLine($"  $env:{KeyMaterial.UsersFileVariable} = \"{usersPath}\"");

        if (persist)
        {
            Console.WriteLine();
            PersistEnvironmentVariables(keyPath, usersPath);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("Those last only for this terminal. Re-run with --persist to set them for your user");
            Console.WriteLine("account permanently.");
        }

        Console.WriteLine();
        Console.WriteLine("Then add an operator:  loom auth add-user operator");
    }

    private static void PersistEnvironmentVariables(string keyPath, string usersPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("--persist writes a Windows user environment variable and does nothing here.");
            Console.WriteLine("Add the exports above to your shell profile instead.");
            return;
        }

        WarnIfDifferentExistingValue(KeyMaterial.KeyFileVariable, keyPath);
        WarnIfDifferentExistingValue(KeyMaterial.UsersFileVariable, usersPath);

        Environment.SetEnvironmentVariable(KeyMaterial.KeyFileVariable, keyPath, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(KeyMaterial.UsersFileVariable, usersPath, EnvironmentVariableTarget.User);

        Console.WriteLine($"Persisted {KeyMaterial.KeyFileVariable} and {KeyMaterial.UsersFileVariable} for your user account.");
        Console.WriteLine("Open a NEW terminal to pick them up - this terminal's environment does not change.");
    }

    private static void WarnIfDifferentExistingValue(string variable, string newValue)
    {
        var existing = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User);
        if (!string.IsNullOrEmpty(existing) && existing != newValue)
        {
            Console.WriteLine($"Warning: {variable} is already set for your user account to '{existing}'.");
            Console.WriteLine($"  It will be replaced with '{newValue}'.");
        }
    }

    public static void AddUser(string username)
    {
        var usersPath = KeyMaterial.ResolveUsersFile();
        if (!File.Exists(usersPath))
        {
            Console.WriteLine($"Users file not found at {usersPath}. Run 'loom auth init' first.");
            return;
        }

        var line = $"{username}:{PasswordHasher.Hash(ReadPassword())}";
        File.AppendAllText(usersPath, line + Environment.NewLine);
        Console.WriteLine($"Added '{username}' to {usersPath}.");
    }

    public static void Hash() => Console.WriteLine(PasswordHasher.Hash(ReadPassword()));

    public static void Token(string subject, bool metricsScope, TimeSpan ttl)
    {
        var key = KeyMaterial.LoadSigningKey(KeyMaterial.ResolveKeyFile());
        var issuer = new JwtIssuer(key, TimeProvider.System);
        Console.WriteLine(issuer.Issue(subject, ttl, metricsScope ? JwtScope.Metrics : JwtScope.Full));
    }

    private static string ReadPassword()
    {
        // Console.ReadKey throws when stdin is redirected, which is every non-interactive
        // path: CI, Docker, config management, `echo pw | loom auth hash`. Fall back to a
        // plain line read there. No prompt is written in that case - it would corrupt the
        // stdout that a caller is capturing.
        if (Console.IsInputRedirected) return Console.ReadLine() ?? string.Empty;

        Console.Write("Password: ");
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
        Console.WriteLine();
        return buffer.ToString();
    }

    /// <summary>Accepts 30d, 12h, 45m. Rejects anything else rather than guessing.</summary>
    public static bool TryParseTtl(string value, out TimeSpan ttl)
    {
        ttl = default;
        if (value.Length < 2) return false;
        if (!int.TryParse(value[..^1], out var n) || n <= 0) return false;
        ttl = value[^1] switch
        {
            'd' => TimeSpan.FromDays(n),
            'h' => TimeSpan.FromHours(n),
            'm' => TimeSpan.FromMinutes(n),
            _ => TimeSpan.Zero
        };
        return ttl > TimeSpan.Zero;
    }
}
