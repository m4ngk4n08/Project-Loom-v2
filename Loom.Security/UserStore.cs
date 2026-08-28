using System.Collections.Frozen;

namespace Loom.Security;

public sealed record UserRecord(string Username, int Iterations, byte[] Salt, byte[] Hash);

/// <summary>Users parsed once at startup from a flat file. No database and no ORM -
/// either would drag reflection into an AOT binary.</summary>
public sealed class UserStore
{
    private readonly FrozenDictionary<string, UserRecord> _users;
    private readonly UserRecord _dummy;

    private UserStore(FrozenDictionary<string, UserRecord> users, UserRecord dummy)
    {
        _users = users;
        _dummy = dummy;
    }

    /// <summary>A malformed line is a startup failure, never a skipped line: a typo must
    /// not silently delete an account. A missing or empty file is also a failure - fail
    /// closed.</summary>
    public static UserStore Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Loom auth: users file not found at '{path}'. Run 'loom auth init'.");

        var users = new Dictionary<string, UserRecord>(StringComparer.Ordinal);
        var lineNumber = 0;

        foreach (var raw in File.ReadLines(path))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var split = line.IndexOf(':');
            if (split <= 0)
                throw new InvalidOperationException($"Loom auth: {path}:{lineNumber} is malformed (expected 'username:hash').");

            var username = line[..split];
            var encoded = line[(split + 1)..];

            if (!PasswordHasher.TryParse(encoded, out var iterations, out var salt, out var hash))
                throw new InvalidOperationException($"Loom auth: {path}:{lineNumber} has an unparseable password hash.");

            if (!users.TryAdd(username, new UserRecord(username, iterations, salt, hash)))
                throw new InvalidOperationException($"Loom auth: {path}:{lineNumber} duplicates user '{username}'.");
        }

        if (users.Count == 0)
            throw new InvalidOperationException($"Loom auth: '{path}' defines no users. Run 'loom auth add-user <name>'.");

        // Fixed dummy record for unknown usernames. Without it, "no such user" returns in
        // microseconds while "wrong password" takes ~74 ms, and the login endpoint becomes
        // a user-enumeration oracle.
        var dummy = new UserRecord(
            "\0dummy",
            PasswordHasher.Iterations,
            new byte[PasswordHasher.SaltBytes],
            new byte[PasswordHasher.HashBytes]);

        return new UserStore(users.ToFrozenDictionary(StringComparer.Ordinal), dummy);
    }

    /// <summary>Always performs exactly one key derivation, whether or not the user
    /// exists. Do not add an early return for the unknown-user case.</summary>
    public bool Verify(string username, string password)
    {
        var known = _users.TryGetValue(username, out var record);
        var target = known ? record! : _dummy;
        var ok = PasswordHasher.Verify(password, target.Iterations, target.Salt, target.Hash);
        return known && ok;
    }
}
