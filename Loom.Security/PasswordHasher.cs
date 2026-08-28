using System.Buffers.Text;
using System.Security.Cryptography;

namespace Loom.Security;

/// <summary>PBKDF2-HMAC-SHA256. Not Argon2: it is not in the BCL, and the available
/// packages would add a dependency with unverified AOT behaviour to defend against a
/// threat this deployment does not face. 600k iterations measures ~74 ms on a dev
/// machine, which is both an acceptable one-off login cost and a cap of roughly 13
/// offline guesses/second/core.</summary>
public static class PasswordHasher
{
    public const int Iterations = 600_000;
    public const int SaltBytes = 16;
    public const int HashBytes = 32;
    private const string Prefix = "pbkdf2-sha256";

    /// <summary>Formats as pbkdf2-sha256$&lt;iterations&gt;$&lt;b64url salt&gt;$&lt;b64url hash&gt;</summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}${Iterations}${Base64Url.EncodeToString(salt)}${Base64Url.EncodeToString(hash)}";
    }

    public static bool TryParse(string encoded, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0; salt = []; hash = [];

        var parts = encoded.Split('$');
        if (parts.Length != 4) return false;
        if (!string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return false;
        if (!int.TryParse(parts[1], out iterations) || iterations <= 0) return false;

        try
        {
            salt = Base64Url.DecodeFromChars(parts[2]);
            hash = Base64Url.DecodeFromChars(parts[3]);
        }
        catch (FormatException) { return false; }

        return salt.Length > 0 && hash.Length > 0;
    }

    /// <summary>Constant-time compare. Never SequenceEqual.</summary>
    public static bool Verify(string password, int iterations, byte[] salt, byte[] expected)
    {
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
