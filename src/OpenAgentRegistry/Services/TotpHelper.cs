using OtpNet;

namespace OpenAgentRegistry.Services;

public static class TotpHelper
{
    public static string NewSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public static string BuildOtpAuthUri(string secret, string accountName, string issuer = "OpenAgentRegistry")
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedAccount = Uri.EscapeDataString(accountName);
        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={secret}&issuer={encodedIssuer}";
    }

    public static bool Verify(string secret, string code, int window = 1)
    {
        var cleaned = code.Trim().Replace(" ", "", StringComparison.Ordinal);
        if (cleaned.Length == 0 || !cleaned.All(char.IsDigit))
            return false;
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(cleaned, out _, new VerificationWindow(window, window));
    }
}
