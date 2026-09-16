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
                // PersistEnvironmentVariablesUnix's failure paths say "Add the exports
                // above" - that instruction is only followable if something was
                // actually printed above it, which this branch (unlike the fresh-key
                // path below) previously never did.
                Console.WriteLine();
                var existingUsersPath = File.Exists(usersPath) ? usersPath : null;
                Console.WriteLine("Set these before starting loom-dashboard:");
                Console.WriteLine(FormatSetVarLine(KeyMaterial.KeyFileVariable, keyPath));
                if (existingUsersPath is not null)
                    Console.WriteLine(FormatSetVarLine(KeyMaterial.UsersFileVariable, existingUsersPath));
                Console.WriteLine();

                // Printed AFTER persisting, from what actually happened - not before,
                // from a guess. On Unix, PersistEnvironmentVariables can carry a
                // pre-existing LOOM_AUTH_USERS_FILE forward out of the shell profile's
                // loom block even though usersPath itself is missing here, so "persisting
                // only the key" would be false whenever that carry-forward fires.
                var persistedUsersPath = PersistEnvironmentVariables(keyPath, existingUsersPath);
                if (existingUsersPath is null)
                {
                    Console.WriteLine(persistedUsersPath is not null
                        ? $"No users file found at {usersPath} - kept the existing {KeyMaterial.UsersFileVariable} already in your shell profile ({persistedUsersPath})."
                        : $"No users file found at {usersPath} - persisting only {KeyMaterial.KeyFileVariable}.");
                }
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
        Console.WriteLine($"Then add an operator:  loom auth add-user operator --users-file {QuoteForCurrentShell(usersPath)}");
    }

    /// <summary>The "set these before starting" line, in the syntax the terminal the user
    /// is actually in will accept - PowerShell on Windows, otherwise whatever
    /// RenderUnixExportLine would put in the shell profile. Printing `$env:` syntax to a
    /// Linux or macOS terminal is not just cosmetic: pasted verbatim, it is a syntax
    /// error there. Routed through the same renderer as the persisted file rather than
    /// its own always-double-quoted format, so a path containing `$` or a backtick can't
    /// print one thing here and write another - copy this line verbatim and it is exactly
    /// as safe as the file loom writes.</summary>
    private static string FormatSetVarLine(string variable, string value)
    {
        if (OperatingSystem.IsWindows()) return $"  $env:{variable} = {QuoteForCurrentShell(value)}";
        var isFish = ClassifyUnixShell(Environment.GetEnvironmentVariable("SHELL")) == "fish";
        return "  " + RenderUnixExportLine(isFish, variable, value);
    }

    /// <summary>Quotes a value for whatever shell the user is actually in, matching
    /// FormatSetVarLine's own per-platform choice, so a printed CLI command (e.g. the
    /// "add-user" line Init prints) can be copied and pasted with the same safety as the
    /// "set these" lines - one path value, one quoting rule, everywhere it is printed.</summary>
    private static string QuoteForCurrentShell(string value)
    {
        if (OperatingSystem.IsWindows()) return $"\"{value}\"";
        var isFish = ClassifyUnixShell(Environment.GetEnvironmentVariable("SHELL")) == "fish";
        return isFish ? QuoteFishSingle(value) : QuotePosixSingle(value);
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

    /// <summary>Returns the users-file path that actually ended up persisted (which, on
    /// Unix, may differ from the usersPath argument via carry-forward - see
    /// PersistEnvironmentVariablesUnix), or null if none was. Callers use this to report
    /// what actually happened rather than what they assumed would happen.</summary>
    private static string? PersistEnvironmentVariables(string keyPath, string? usersPath)
    {
        if (!OperatingSystem.IsWindows())
            return PersistEnvironmentVariablesUnix(keyPath, usersPath);

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
        return usersPath;
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
    private static string? PersistEnvironmentVariablesUnix(string keyPath, string? usersPath)
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
            return null;
        }

        var profilePath = ResolveUnixProfilePath(shellEnvValue, homeDirectory, OperatingSystem.IsMacOS(), Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));
        string? effectiveUsersPath;

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

            // The re-run path (key exists, users file missing) calls here with
            // usersPath: null. RenderUnixPersistBlock then omits the users line
            // entirely, and UpsertUnixPersistBlock replaces the WHOLE block - so a
            // LOOM_AUTH_USERS_FILE export the user was relying on would silently vanish
            // unless we carry it forward from whatever block is already there.
            effectiveUsersPath = usersPath ?? ExtractExistingUsersPath(existing);
            var block = RenderUnixPersistBlock(shellEnvValue, keyPath, effectiveUsersPath);

            // Computed after effectiveUsersPath, and skips LOOM_AUTH_USERS_FILE when the
            // block about to be written omits it entirely - warning about a variable the
            // block does not assign is not just noise, the second wording below ("the
            // block added below will take precedence") is flatly false when nothing below
            // assigns it.
            foreach (var variable in FindVariablesAssignedOutsideBlock(existing))
            {
                if (variable == KeyMaterial.UsersFileVariable && effectiveUsersPath is null) continue;

                Console.WriteLine(OutsideAssignmentWinsOverLoom(existing, variable)
                    ? $"Warning: {profilePath} already assigns {variable} outside loom's block, later in the file - that existing line will take precedence over the block below."
                    : $"Warning: {profilePath} already assigns {variable} outside loom's block - the block added below will take precedence.");
            }

            var updatedBytes = UpsertUnixPersistBlockBytes(existingBytes, existing, block);

            // Many people's shell profile is a symlink into a dotfiles repo (stow,
            // chezmoi, a plain git repo). File.Move REPLACES the target rather than
            // writing through it - unlinking the symlink and dropping a regular file in
            // its place, silently detaching the user's setup. Resolve to the final
            // target first and write-then-rename there instead; a non-symlink
            // profilePath resolves to itself (ResolveLinkTarget returns null).
            var writeTargetPath = profilePath;
            if (File.Exists(profilePath))
            {
                var resolvedTarget = File.ResolveLinkTarget(profilePath, returnFinalTarget: true);
                if (resolvedTarget is not null) writeTargetPath = resolvedTarget.FullName;
            }

            // File.Move also does not preserve the replaced file's permissions - a 600
            // profile would come back 644. Capture the current mode and re-apply it to
            // the temp file before the rename so it survives. The !IsWindows() guard is
            // redundant with the caller's (this method only runs on Unix) but is what
            // the platform-compat analyzer needs to see directly around a Unix-only
            // API to accept the call - see TightenIfLoose above for the same pattern.
            UnixFileMode? existingMode = null;
            if (!OperatingSystem.IsWindows() && File.Exists(writeTargetPath))
                existingMode = File.GetUnixFileMode(writeTargetPath);

            // Write-then-rename rather than truncate-in-place, so a crash mid-write
            // leaves either the old file or the new one intact, never a truncated shell
            // profile. File.Move's overwrite is atomic on the same filesystem, and the
            // temp file sits next to the target so it always is one.
            var tempPath = writeTargetPath + $".loom-tmp-{Guid.NewGuid():N}";
            try
            {
                File.WriteAllBytes(tempPath, updatedBytes);
                if (!OperatingSystem.IsWindows() && existingMode is not null)
                    File.SetUnixFileMode(tempPath, existingMode.Value);
                File.Move(tempPath, writeTargetPath, overwrite: true);
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
            return null;
        }

        Console.WriteLine($"This terminal's environment does not change - open a new terminal or run `source {profilePath}` to pick them up.");
        return effectiveUsersPath;
    }

    /// <summary>Pure. Maps $SHELL's basename to the profile file loom persists into.
    /// Unknown or unset shells fall back to ~/.profile rather than guessing. isMacOS is
    /// an explicit parameter, not an internal OperatingSystem.IsMacOS() read, so this
    /// stays pure and testable for both branches on any host - the caller passes what
    /// it detects.</summary>
    public static string ResolveUnixProfilePath(string? shellEnvValue, string homeDirectory, bool isMacOS, string? xdgConfigHome = null)
    {
        var home = homeDirectory.TrimEnd('/');
        return ClassifyUnixShell(shellEnvValue) switch
        {
            "zsh" => $"{home}/.zshrc",
            // Terminal.app and iTerm start bash as a login shell on macOS, which reads
            // .bash_profile (or .bash_login / .profile) and never .bashrc - a stock
            // .bash_profile does not source .bashrc either. Linux interactive bash reads
            // .bashrc.
            "bash" => isMacOS ? $"{home}/.bash_profile" : $"{home}/.bashrc",
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
    /// already assigned to LOOM_AUTH_USERS_FILE out of it, in either `export VAR=...`
    /// or fish's `set -gx VAR ...` form, across every form loom (or a human) may have
    /// written: single-quoted with POSIX '\''-escaping (the current writer),
    /// double-quoted, and bare/unquoted. A naive [^'"]* scan (the previous approach)
    /// truncated at the FIRST embedded quote, so a path containing an apostrophe - now
    /// written as '/home/u/a'\''b/users' - extracted as just "/home/u/a". Returns null
    /// when there is no block or no such line - the caller then has nothing to
    /// preserve.</summary>
    public static string? ExtractExistingUsersPath(string existingContent)
    {
        var blockPattern = new Regex(
            Regex.Escape(UnixBlockStart) + @".*?" + Regex.Escape(UnixBlockEnd),
            RegexOptions.Singleline);
        var blockMatch = blockPattern.Match(existingContent);
        if (!blockMatch.Success) return null;

        var assignment = Regex.Match(blockMatch.Value, Regex.Escape(KeyMaterial.UsersFileVariable) + "[ =]+");
        return assignment.Success ? ParseShellValue(blockMatch.Value, assignment.Index + assignment.Length) : null;
    }

    /// <summary>Pure. Parses one shell value starting at valueStart: a single-quoted
    /// value understanding BOTH quoting conventions this file writes - POSIX's
    /// close-escape-reopen splice ('\'') from QuotePosixSingle, and fish's in-string
    /// backslash escapes (\\ and \') from QuoteFishSingle - a double-quoted value with
    /// \" and \\ unescaped, or - if valueStart is neither quote character - a bare
    /// value running to end of line. Returns null on an unterminated quote, which reads
    /// as "nothing to preserve" rather than a mangled partial value.</summary>
    private static string? ParseShellValue(string content, int valueStart)
    {
        if (valueStart >= content.Length) return null;

        if (content[valueStart] == '\'')
        {
            var sb = new StringBuilder();
            var pos = valueStart + 1;
            while (true)
            {
                if (pos >= content.Length) return null;

                // '\'' - the standard POSIX splice for a literal quote: close (this
                // quote), an escaped literal quote outside any quoting (\'), then
                // reopen (a 4th quote char) - four characters total, not the value's
                // end. Checked before treating '\'' as a fish escape, since this
                // exact 4-char run is what QuotePosixSingle emits for an apostrophe.
                if (content[pos] == '\'' && pos + 3 < content.Length && content[pos + 1] == '\\' && content[pos + 2] == '\'' && content[pos + 3] == '\'')
                {
                    sb.Append('\'');
                    pos += 4;
                    continue;
                }

                if (content[pos] == '\'') return sb.ToString();

                // \\ and \' - fish's in-string escapes (QuoteFishSingle): everything
                // else, $ and backticks included, is literal inside fish's single
                // quotes, so only these two backslash pairs unescape.
                if (content[pos] == '\\' && pos + 1 < content.Length && (content[pos + 1] == '\\' || content[pos + 1] == '\''))
                {
                    sb.Append(content[pos + 1]);
                    pos += 2;
                    continue;
                }

                sb.Append(content[pos]);
                pos++;
            }
        }

        if (content[valueStart] == '"')
        {
            var sb = new StringBuilder();
            var pos = valueStart + 1;
            while (pos < content.Length)
            {
                var c = content[pos];
                if (c == '"') return sb.ToString();
                if (c == '\\' && pos + 1 < content.Length && (content[pos + 1] == '"' || content[pos + 1] == '\\'))
                {
                    sb.Append(content[pos + 1]);
                    pos += 2;
                    continue;
                }
                sb.Append(c);
                pos++;
            }
            return null;
        }

        var end = content.IndexOfAny(new[] { '\r', '\n' }, valueStart);
        var rawValue = end < 0 ? content[valueStart..] : content[valueStart..end];
        return rawValue.TrimEnd();
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

    /// <summary>Pure. True if a hand-written assignment to `variable` OUTSIDE the marked
    /// block will win over loom's own block once PersistEnvironmentVariablesUnix writes
    /// it - i.e. the shell executes it last. UpsertUnixPersistBlock replaces an EXISTING
    /// block IN PLACE, at the position of the first occurrence, so any outside
    /// assignment that ends up positioned after that wins. With no existing block, loom's
    /// is appended at the very end of the file and always wins - nothing can come after
    /// it.</summary>
    public static bool OutsideAssignmentWinsOverLoom(string existingContent, string variable)
    {
        var blockPattern = new Regex(
            Regex.Escape(UnixBlockStart) + @".*?" + Regex.Escape(UnixBlockEnd) + @"\r?\n?",
            RegexOptions.Singleline);
        var blockMatches = blockPattern.Matches(existingContent);
        if (blockMatches.Count == 0) return false;

        var loomBlockEnd = blockMatches[0].Index + blockMatches[0].Length;

        var assignmentPattern = new Regex(@"(?m)^\s*(export\s+|set\s+(-gx|-x)\s+)?" + Regex.Escape(variable) + @"\b\s*[= ]");
        foreach (Match m in assignmentPattern.Matches(existingContent))
        {
            if (m.Index < loomBlockEnd) continue;

            var insideAnyBlock = false;
            foreach (Match b in blockMatches)
            {
                if (m.Index >= b.Index && m.Index < b.Index + b.Length) { insideAnyBlock = true; break; }
            }
            if (!insideAnyBlock) return true;
        }
        return false;
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

    /// <summary>Byte-level counterpart of UpsertUnixPersistBlock, used for the actual
    /// write. existingBytes are spliced verbatim - never round-tripped through any
    /// encoding - and only the newly inserted block is encoded, as UTF-8. Re-encoding
    /// the whole merged string with Latin1.GetBytes (the previous approach) would
    /// mangle loom's OWN block whenever a path contains anything above ASCII:
    /// Latin1.GetBytes emits one byte per UTF-16 code point, so 'é' (U+00E9 - UTF-8
    /// 0xC3 0xA9 in a real path) comes back as the single byte 0xE9, and anything above
    /// U+00FF (Cyrillic, CJK) silently becomes '?'. existingLatin1 - the Latin1 decoding
    /// of existingBytes - is used only to locate the block via regex; that is safe
    /// because Latin1 decode/encode is a 1:1 byte&lt;-&gt;codepoint mapping, so a match
    /// index in that string is the identical byte offset in existingBytes.</summary>
    public static byte[] UpsertUnixPersistBlockBytes(byte[] existingBytes, string existingLatin1, string block)
    {
        var pattern = new Regex(
            Regex.Escape(UnixBlockStart) + @".*?" + Regex.Escape(UnixBlockEnd) + @"\r?\n?",
            RegexOptions.Singleline);
        var matches = pattern.Matches(existingLatin1);
        var blockBytes = Encoding.UTF8.GetBytes(block);

        using var result = new MemoryStream();
        if (matches.Count == 0)
        {
            result.Write(existingBytes, 0, existingBytes.Length);
            var needsSeparator = existingBytes.Length > 0 && existingBytes[^1] != (byte)'\n';
            if (needsSeparator) result.WriteByte((byte)'\n');
            result.Write(blockBytes, 0, blockBytes.Length);
            return result.ToArray();
        }

        var lastEnd = 0;
        var replaced = false;
        foreach (Match m in matches)
        {
            result.Write(existingBytes, lastEnd, m.Index - lastEnd);
            if (!replaced)
            {
                result.Write(blockBytes, 0, blockBytes.Length);
                replaced = true;
            }
            lastEnd = m.Index + m.Length;
        }
        result.Write(existingBytes, lastEnd, existingBytes.Length - lastEnd);
        return result.ToArray();
    }

    /// <summary>AddUser/Token-only resolution, deliberately more forgiving than
    /// KeyMaterial.ResolveUsersFile(): env var, else the system default if that file
    /// exists, else DevSecretsDirectory. Without this, `loom auth init` (no env var, no
    /// --persist) writes to dev-secrets while the system default on Unix stays
    /// /var/secrets/loom, so the very next `loom auth add-user` the tool just told the
    /// user to run fails - a closed loop. This fallback must never reach
    /// SecurityServiceExtensions: the CLI is a developer tool and can afford to guess
    /// where your own notes live, the host is the security boundary and must not.</summary>
    private static string ResolveUsersFileForCli()
    {
        var envValue = Environment.GetEnvironmentVariable(KeyMaterial.UsersFileVariable);
        if (envValue is not null) return envValue;
        if (File.Exists(KeyMaterial.DefaultUsersFile)) return KeyMaterial.DefaultUsersFile;

        var devPath = Path.Combine(DevSecretsDirectory, "users");
        Console.Error.WriteLine($"{KeyMaterial.UsersFileVariable} is not set and no users file exists at the system default - using {devPath}.");
        return devPath;
    }

    /// <summary>Token-only resolution mirroring ResolveUsersFileForCli - see its remarks.</summary>
    private static string ResolveKeyFileForCli()
    {
        var envValue = Environment.GetEnvironmentVariable(KeyMaterial.KeyFileVariable);
        if (envValue is not null) return envValue;
        if (File.Exists(KeyMaterial.DefaultKeyFile)) return KeyMaterial.DefaultKeyFile;

        var devPath = Path.Combine(DevSecretsDirectory, "jwt.key");
        Console.Error.WriteLine($"{KeyMaterial.KeyFileVariable} is not set and no key file exists at the system default - using {devPath}.");
        return devPath;
    }

    /// <summary>usersFile, when given (--users-file), is used exactly as supplied - no
    /// fallback, no search, no environment lookup. Only when it is null does resolution
    /// fall back to ResolveUsersFileForCli's guess.</summary>
    public static void AddUser(string username, string? usersFile = null)
    {
        var usersPath = usersFile ?? ResolveUsersFileForCli();
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

    /// <summary>keyFile, when given (--key-file), is used exactly as supplied - no
    /// fallback, no search, no environment lookup. Only when it is null does resolution
    /// fall back to ResolveKeyFileForCli's guess. stdout here is the product - a caller
    /// redirects it straight to a token file - so the not-found message goes to stderr,
    /// not stdout, and this never calls KeyMaterial.LoadSigningKey against a path known
    /// not to exist, which would otherwise surface as an uncaught exception.</summary>
    public static void Token(string subject, JwtScope scope, TimeSpan ttl, string? keyFile = null)
    {
        var keyPath = keyFile ?? ResolveKeyFileForCli();
        if (keyFile is not null && !File.Exists(keyPath))
        {
            Console.Error.WriteLine($"Key file not found at {keyPath}.");
            return;
        }

        var key = KeyMaterial.LoadSigningKey(keyPath);
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
