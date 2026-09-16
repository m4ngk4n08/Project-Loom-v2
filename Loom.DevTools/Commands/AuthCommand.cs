using Loom.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Loom.DevTools.Commands;

/// <summary>loom auth init | add-user &lt;name&gt; | hash | token --sub X [--scope metrics] [--ttl 90d]</summary>
public static class AuthCommand
{
    private static string DevSecretsDirectory => KeyMaterial.DevSecretsDirectory;

    // Unix only - these APIs throw PlatformNotSupportedException on Windows, where
    // %LOCALAPPDATA% is already per-user and needs no tightening.
    private const UnixFileMode SecretDirMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode SecretFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void Init(bool persist = false)
    {
        EnsureDevSecretsDirectory();
        var keyPath = Path.Combine(DevSecretsDirectory, "jwt.key");
        var usersPath = Path.Combine(DevSecretsDirectory, "users");

        if (File.Exists(keyPath))
        {
            TightenIfLoose(keyPath, SecretFileMode);
            if (File.Exists(usersPath)) TightenIfLoose(usersPath, SecretFileMode);

            Console.WriteLine($"Refusing to overwrite an existing signing key at {keyPath}.");
            Console.WriteLine("Delete it deliberately if you intend to rotate - every outstanding token dies with it.");

            if (persist)
            {
                Console.WriteLine();
                var existingUsersPath = File.Exists(usersPath) ? usersPath : null;
                if (existingUsersPath is null)
                    Console.WriteLine($"No users file found at {usersPath} - persisting only {KeyMaterial.KeyFileVariable}.");
                PersistEnvironmentVariables(keyPath, existingUsersPath);
            }

            return;
        }

        WriteSecretFile(keyPath, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        if (!File.Exists(usersPath)) WriteSecretFile(usersPath, "# username:pbkdf2-sha256$...\n");
        else TightenIfLoose(usersPath, SecretFileMode);

        Console.WriteLine($"Wrote {keyPath}");
        Console.WriteLine($"Wrote {usersPath}");
        Console.WriteLine();
        Console.WriteLine("Set these before starting loom-dashboard:");
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

    /// <summary>Creates dev-secrets at 700 on Unix. If it already exists (a re-run, or a
    /// leftover from before this fix), tightens it in place rather than trusting the mode
    /// it already has.</summary>
    private static void EnsureDevSecretsDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(DevSecretsDirectory);
            return;
        }

        if (Directory.Exists(DevSecretsDirectory))
            TightenIfLoose(DevSecretsDirectory, SecretDirMode);
        else
            Directory.CreateDirectory(DevSecretsDirectory, SecretDirMode);
    }

    /// <summary>Creates a new file at 600 on Unix by passing the mode to the OS at create
    /// time, so the key is never observable on disk with looser permissions - a
    /// WriteAllText followed by a chmod leaves exactly that window open. On Windows,
    /// permissions are left alone.</summary>
    private static void WriteSecretFile(string path, string content)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, content);
            return;
        }

        using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            UnixCreateMode = SecretFileMode,
        });
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    /// <summary>Unix only. A user who ran a previous version of this tool has a
    /// world-readable key sitting on disk right now with no way to know - tighten it and
    /// say so.</summary>
    private static void TightenIfLoose(string path, UnixFileMode required)
    {
        if (OperatingSystem.IsWindows()) return;

        var current = File.GetUnixFileMode(path);
        if ((current & ~required) != 0)
        {
            File.SetUnixFileMode(path, required);
            Console.WriteLine($"Tightened permissions on {path} to {ModeString(required)} (was {ModeString(current)}).");
        }
    }

    private static string ModeString(UnixFileMode mode)
    {
        var octal = 0;
        if (mode.HasFlag(UnixFileMode.UserRead)) octal += 400;
        if (mode.HasFlag(UnixFileMode.UserWrite)) octal += 200;
        if (mode.HasFlag(UnixFileMode.UserExecute)) octal += 100;
        if (mode.HasFlag(UnixFileMode.GroupRead)) octal += 40;
        if (mode.HasFlag(UnixFileMode.GroupWrite)) octal += 20;
        if (mode.HasFlag(UnixFileMode.GroupExecute)) octal += 10;
        if (mode.HasFlag(UnixFileMode.OtherRead)) octal += 4;
        if (mode.HasFlag(UnixFileMode.OtherWrite)) octal += 2;
        if (mode.HasFlag(UnixFileMode.OtherExecute)) octal += 1;
        return octal.ToString("D3");
    }

    private static void PersistEnvironmentVariables(string keyPath, string? usersPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            PersistEnvironmentVariablesUnix(keyPath, usersPath);
            return;
        }

        WarnIfDifferentExistingValue(KeyMaterial.KeyFileVariable, keyPath);
        Environment.SetEnvironmentVariable(KeyMaterial.KeyFileVariable, keyPath, EnvironmentVariableTarget.User);

        if (usersPath is null)
        {
            Console.WriteLine($"Persisted {KeyMaterial.KeyFileVariable} for your user account.");
        }
        else
        {
            WarnIfDifferentExistingValue(KeyMaterial.UsersFileVariable, usersPath);
            Environment.SetEnvironmentVariable(KeyMaterial.UsersFileVariable, usersPath, EnvironmentVariableTarget.User);
            Console.WriteLine($"Persisted {KeyMaterial.KeyFileVariable} and {KeyMaterial.UsersFileVariable} for your user account.");
        }

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

    private const string UnixBlockStart = "# >>> loom >>>";
    private const string UnixBlockEnd = "# <<< loom <<<";

    /// <summary>Unix has no per-user environment store for .NET to write to -
    /// EnvironmentVariableTarget.User is a Windows/registry concept. The only durable
    /// place is a shell startup file, chosen from $SHELL's basename so it matches the
    /// shell the user actually runs.</summary>
    private static void PersistEnvironmentVariablesUnix(string keyPath, string? usersPath)
    {
        var shellEnvValue = Environment.GetEnvironmentVariable("SHELL");

        // HOME unset or empty must never fall back to a literal "~" - Directory.CreateDirectory
        // would then create a real directory named "~" in the working directory, and the
        // profile path printed would be junk that does not match where LocalApplicationData
        // (via getpwuid) actually resolved the key to.
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(homeDirectory) || !Path.IsPathRooted(homeDirectory))
        {
            Console.WriteLine("Could not determine your home directory - refusing to guess a shell profile path.");
            Console.WriteLine("Add the exports above to your shell profile manually.");
            return;
        }

        var profilePath = ResolveUnixProfilePath(shellEnvValue, homeDirectory);
        var block = RenderUnixPersistBlock(shellEnvValue, keyPath, usersPath);

        try
        {
            var directory = Path.GetDirectoryName(profilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var existing = File.Exists(profilePath) ? File.ReadAllText(profilePath) : string.Empty;
            var updated = UpsertUnixPersistBlock(existing, block);
            File.WriteAllText(profilePath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Console.WriteLine($"Wrote to {profilePath}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not write {profilePath}: {ex.Message}");
            Console.WriteLine("Add the exports above to your shell profile manually.");
            return;
        }

        Console.WriteLine($"This terminal's environment does not change - open a new terminal or run `source {profilePath}` to pick them up.");
    }

    /// <summary>Pure. Maps $SHELL's basename to the profile file loom persists into.
    /// Unknown or unset shells fall back to ~/.profile rather than guessing.</summary>
    public static string ResolveUnixProfilePath(string? shellEnvValue, string homeDirectory)
    {
        var home = homeDirectory.TrimEnd('/');
        return ClassifyUnixShell(shellEnvValue) switch
        {
            "zsh" => $"{home}/.zshrc",
            "bash" => $"{home}/.bashrc",
            "fish" => $"{home}/.config/fish/config.fish",
            _ => $"{home}/.profile",
        };
    }

    /// <summary>Pure. Renders the marked block for the given shell. fish needs
    /// `set -gx VAR "value"` - `export VAR="value"` is a syntax error there, and writing
    /// the wrong form breaks the user's shell silently, the next time they open a
    /// terminal.</summary>
    public static string RenderUnixPersistBlock(string? shellEnvValue, string keyPath, string? usersPath)
    {
        var isFish = ClassifyUnixShell(shellEnvValue) == "fish";
        var sb = new StringBuilder();
        sb.Append(UnixBlockStart).Append('\n');
        sb.Append(RenderUnixExportLine(isFish, KeyMaterial.KeyFileVariable, keyPath)).Append('\n');
        if (usersPath is not null)
            sb.Append(RenderUnixExportLine(isFish, KeyMaterial.UsersFileVariable, usersPath)).Append('\n');
        sb.Append(UnixBlockEnd).Append('\n');
        return sb.ToString();
    }

    private static string RenderUnixExportLine(bool isFish, string variable, string value) =>
        isFish ? $"set -gx {variable} \"{value}\"" : $"export {variable}=\"{value}\"";

    private static string ClassifyUnixShell(string? shellEnvValue)
    {
        if (string.IsNullOrEmpty(shellEnvValue)) return "other";
        var lastSlash = shellEnvValue.LastIndexOf('/');
        var name = lastSlash >= 0 ? shellEnvValue[(lastSlash + 1)..] : shellEnvValue;
        return name is "zsh" or "bash" or "fish" ? name : "other";
    }

    /// <summary>Pure. Idempotent upsert of the marked loom block into existing shell
    /// profile content. Content outside the block survives byte-for-byte; if the block
    /// already exists its position is preserved and it is replaced in place; if it
    /// appears more than once (hand-edited, or an earlier buggy run) every occurrence
    /// collapses to one, at the position of the first. Never touches individual
    /// `export LOOM_...` lines directly - there is no way to tell loom's own line from
    /// one the user wrote deliberately, so only the marked block is ever touched.</summary>
    public static string UpsertUnixPersistBlock(string existingContent, string block)
    {
        var pattern = new Regex(
            Regex.Escape(UnixBlockStart) + @".*?" + Regex.Escape(UnixBlockEnd) + @"\r?\n?",
            RegexOptions.Singleline);
        var matches = pattern.Matches(existingContent);

        if (matches.Count == 0)
        {
            var separator = existingContent.Length == 0 || existingContent.EndsWith('\n') ? "" : "\n";
            return existingContent + separator + block;
        }

        var sb = new StringBuilder();
        var lastEnd = 0;
        var replaced = false;
        foreach (Match m in matches)
        {
            sb.Append(existingContent, lastEnd, m.Index - lastEnd);
            if (!replaced)
            {
                sb.Append(block);
                replaced = true;
            }
            lastEnd = m.Index + m.Length;
        }
        sb.Append(existingContent, lastEnd, existingContent.Length - lastEnd);
        return sb.ToString();
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

    public static void Token(string subject, JwtScope scope, TimeSpan ttl)
    {
        var key = KeyMaterial.LoadSigningKey(KeyMaterial.ResolveKeyFile());
        var issuer = new JwtIssuer(key, TimeProvider.System);
        Console.WriteLine(issuer.Issue(subject, ttl, scope));
    }

    /// <summary>Pure. Strips a leading UTF-8 BOM from a password read off redirected stdin.
    /// PowerShell prepends U+FEFF to a piped stream, and Console.ReadLine hands it back as
    /// the first character - so `"pw" | loom auth add-user x` used to hash the wrong string and
    /// create an account whose password nothing could ever match. Same three bytes as the
    /// Set-Content BOM trap in CLAUDE.md, arriving on stdin instead of in a file.</summary>
    public static string NormalizePipedPassword(string? line) =>
        (line ?? string.Empty).TrimStart((char)0xFEFF);

    private static string ReadPassword()
    {
        // Console.ReadKey throws when stdin is redirected, which is every non-interactive
        // path: CI, Docker, config management, `echo pw | loom auth hash`. Fall back to a
        // plain line read there. No prompt is written in that case - it would corrupt the
        // stdout that a caller is capturing.
        if (Console.IsInputRedirected) return NormalizePipedPassword(Console.ReadLine());

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

    /// <summary>Accepts "metrics" and "full". Rejects anything else rather than guessing.
    /// A typo must not silently widen authority: an unvalidated comparison against "metrics"
    /// turns `--scope metrcs` into a full-scope token, which for a 90-day service credential
    /// hands an unattended scraper full operator authority.</summary>
    public static bool TryParseScope(string value, out JwtScope scope)
    {
        scope = JwtScope.Full;
        switch (value)
        {
            case "metrics": scope = JwtScope.Metrics; return true;
            case "full": scope = JwtScope.Full; return true;
            default: return false;
        }
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
