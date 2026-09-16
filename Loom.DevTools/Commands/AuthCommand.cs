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
        Console.WriteLine(FormatSetVarLine(KeyMaterial.KeyFileVariable, keyPath));
        Console.WriteLine(FormatSetVarLine(KeyMaterial.UsersFileVariable, usersPath));

        if (persist)
        {
            Console.WriteLine();
            PersistEnvironmentVariables(keyPath, usersPath);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("Those last only for this terminal. Re-run with --persist to set them permanently.");
        }

        Console.WriteLine();
        Console.WriteLine("Then add an operator:  loom auth add-user operator");
    }

    /// <summary>The "set these before starting" line, in the syntax the terminal the user
    /// is actually in will accept - PowerShell on Windows, fish's `set -gx` when $SHELL
    /// says fish, POSIX `export` otherwise. Printing `$env:` syntax to a Linux or macOS
    /// terminal is not just cosmetic: pasted verbatim, it is a syntax error there.</summary>
    private static string FormatSetVarLine(string variable, string value)
    {
        if (OperatingSystem.IsWindows()) return $"  $env:{variable} = \"{value}\"";
        if (ClassifyUnixShell(Environment.GetEnvironmentVariable("SHELL")) == "fish")
            return $"  set -gx {variable} \"{value}\"";
        return $"  export {variable}=\"{value}\"";
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

        var profilePath = ResolveUnixProfilePath(shellEnvValue, homeDirectory, Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));

        try
        {
            var directory = Path.GetDirectoryName(profilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Latin-1 maps every byte 0-255 to the identically-numbered code point, so
            // decoding and later re-encoding with it round-trips the file's bytes exactly
            // - unlike UTF-8, which decodes an invalid or non-UTF-8 sequence (a latin-1
            // .bashrc with accented comments, say) as U+FFFD and then WRITES BACK that
            // replacement character, permanently corrupting bytes we never needed to
            // understand. Our own block text is pure ASCII, so this is transparent to it.
            var existingBytes = File.Exists(profilePath) ? File.ReadAllBytes(profilePath) : [];
            var existing = Encoding.Latin1.GetString(existingBytes);

            foreach (var variable in FindVariablesAssignedOutsideBlock(existing))
                Console.WriteLine($"Warning: {profilePath} already assigns {variable} outside loom's block - the block added below will take precedence.");

            // The re-run path (key exists, users file missing) calls here with
            // usersPath: null. RenderUnixPersistBlock then omits the users line
            // entirely, and UpsertUnixPersistBlock replaces the WHOLE block - so a
            // LOOM_AUTH_USERS_FILE export the user was relying on would silently vanish
            // unless we carry it forward from whatever block is already there.
            var effectiveUsersPath = usersPath ?? ExtractExistingUsersPath(existing);
            var block = RenderUnixPersistBlock(shellEnvValue, keyPath, effectiveUsersPath);

            var updated = UpsertUnixPersistBlock(existing, block);
            var updatedBytes = Encoding.Latin1.GetBytes(updated);

            // Write-then-rename rather than truncate-in-place, so a crash mid-write
            // leaves either the old file or the new one intact, never a truncated shell
            // profile. File.Move's overwrite is atomic on the same filesystem, and the
            // temp file sits next to the target so it always is one.
            var tempPath = profilePath + $".loom-tmp-{Guid.NewGuid():N}";
            try
            {
                File.WriteAllBytes(tempPath, updatedBytes);
                File.Move(tempPath, profilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }

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
    public static string ResolveUnixProfilePath(string? shellEnvValue, string homeDirectory, string? xdgConfigHome = null)
    {
        var home = homeDirectory.TrimEnd('/');
        return ClassifyUnixShell(shellEnvValue) switch
        {
            "zsh" => $"{home}/.zshrc",
            // Terminal.app and iTerm start bash as a login shell on macOS, which reads
            // .bash_profile (or .bash_login / .profile) and never .bashrc - a stock
            // .bash_profile does not source .bashrc either. Linux interactive bash reads
            // .bashrc.
            "bash" => OperatingSystem.IsMacOS() ? $"{home}/.bash_profile" : $"{home}/.bashrc",
            // fish reads its config from $XDG_CONFIG_HOME/fish, not a hardcoded ~/.config -
            // a fish user with XDG_CONFIG_HOME set elsewhere would otherwise get a file
            // fish never reads, reported as success.
            "fish" => $"{ResolveConfigHome(xdgConfigHome, home)}/fish/config.fish",
            _ => $"{home}/.profile",
        };
    }

    private static string ResolveConfigHome(string? xdgConfigHome, string home) =>
        string.IsNullOrEmpty(xdgConfigHome) ? $"{home}/.config" : xdgConfigHome.TrimEnd('/');

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

    /// <summary>Single-quoted, not double-quoted: the value is a filesystem path that can
    /// come from an environment variable (XDG_DATA_HOME feeds LocalApplicationData), so a
    /// backtick, `$`, or embedded `"` inside a double-quoted rc line would either set the
    /// wrong value or run a command substitution on every new shell - worse than a wrong
    /// path.</summary>
    private static string RenderUnixExportLine(bool isFish, string variable, string value) =>
        isFish ? $"set -gx {variable} {QuoteFishSingle(value)}" : $"export {variable}={QuotePosixSingle(value)}";

    /// <summary>POSIX single quotes admit no escape at all - the standard trick to
    /// include a literal quote is to close the quoted string, splice in an escaped quote,
    /// and reopen: 'it'\''s' for it's.</summary>
    private static string QuotePosixSingle(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>fish's single-quoted strings recognize \\ and \' as escapes (everything
    /// else, `$` and backticks included, is literal) - the opposite convention from
    /// POSIX, so it needs its own escaping rather than reusing QuotePosixSingle.</summary>
    private static string QuoteFishSingle(string value) =>
        "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    /// <summary>Pure. Finds the first loom block in existingContent and pulls the value
    /// already assigned to LOOM_AUTH_USERS_FILE out of it, in either `export VAR="..."`
    /// or fish's `set -gx VAR "..."` form. Returns null when there is no block or no such
    /// line - the caller then has nothing to preserve.</summary>
    public static string? ExtractExistingUsersPath(string existingContent)
    {
        var blockPattern = new Regex(
            Regex.Escape(UnixBlockStart) + @".*?" + Regex.Escape(UnixBlockEnd),
            RegexOptions.Singleline);
        var blockMatch = blockPattern.Match(existingContent);
        if (!blockMatch.Success) return null;

        // Matches both the current single-quoted form and the double-quoted form written
        // by loom before item 7's quoting fix, so a block written by an older loom is
        // still recognized.
        var lineMatch = Regex.Match(blockMatch.Value, Regex.Escape(KeyMaterial.UsersFileVariable) + "[ =]+['\"]([^'\"]*)['\"]");
        return lineMatch.Success ? lineMatch.Groups[1].Value : null;
    }

    /// <summary>Pure. Detects a hand-written assignment to either loom variable OUTSIDE
    /// the marked block - e.g. `export LOOM_JWT_KEY_FILE=/my/own/key` earlier in the same
    /// file - so it can be warned about rather than silently shadowed. Never edits
    /// anything outside the block; this is read-only.</summary>
    public static IReadOnlyList<string> FindVariablesAssignedOutsideBlock(string existingContent)
    {
        var blockPattern = new Regex(
            Regex.Escape(UnixBlockStart) + @".*?" + Regex.Escape(UnixBlockEnd) + @"\r?\n?",
            RegexOptions.Singleline);
        var outside = blockPattern.Replace(existingContent, "");

        var found = new List<string>();
        foreach (var variable in new[] { KeyMaterial.KeyFileVariable, KeyMaterial.UsersFileVariable })
        {
            var assignmentPattern = @"(?m)^\s*(export\s+|set\s+(-gx|-x)\s+)?" + Regex.Escape(variable) + @"\b\s*[= ]";
            if (Regex.IsMatch(outside, assignmentPattern))
                found.Add(variable);
        }
        return found;
    }

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
