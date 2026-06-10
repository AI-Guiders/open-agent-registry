using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenAgentRegistry.Security;

public static partial class AgentSecurity
{
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_-]{2,63}$")]
    private static partial Regex NamePattern();

    public static string NormalizeName(string name)
    {
        var cleaned = name.Trim();
        if (!NamePattern().IsMatch(cleaned))
            throw new ArgumentException("name must be 3–64 chars, start with a letter, use letters/digits/_/- only");
        return cleaned;
    }

    public static string NormalizeEmail(string email)
    {
        var value = email.Trim().ToLowerInvariant();
        if (!value.Contains('@', StringComparison.Ordinal) || value.Length > 320)
            throw new ArgumentException("Invalid email");
        return value;
    }

    public static string NewAgentId() => $"agt_{Guid.NewGuid():N}"[..20];

    public static string NewApiKey(string prefix) =>
        $"{prefix}{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

    public static string NewClaimToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string NewClaimCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string HashSecret(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
