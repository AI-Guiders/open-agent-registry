using Microsoft.Extensions.Configuration;
using OpenAgentRegistry.Options;

namespace OpenAgentRegistry.Configuration;

public static class RegistryEnvironment
{
    public static void ApplyEnvironmentOverrides(RegistryOptions options)
    {
        options.PublicBaseUrl = Get("OAR_PUBLIC_BASE_URL") ?? options.PublicBaseUrl;
        options.DatabasePath = Get("OAR_DATABASE_PATH") ?? options.DatabasePath;
        options.DevExposeClaimCodes = GetBool("OAR_DEV_EXPOSE_CLAIM_CODES") ?? options.DevExposeClaimCodes;
        options.DevExposeTotpSecret = GetBool("OAR_DEV_EXPOSE_TOTP_SECRET") ?? options.DevExposeTotpSecret;
        options.ClaimRequire2Fa = GetBool("OAR_CLAIM_REQUIRE_2FA") ?? options.ClaimRequire2Fa;
        options.SmtpHost = Get("OAR_SMTP_HOST") ?? options.SmtpHost;
        options.SmtpPort = GetInt("OAR_SMTP_PORT") ?? options.SmtpPort;
        options.SmtpUser = Get("OAR_SMTP_USER") ?? options.SmtpUser;
        options.SmtpPassword = Get("OAR_SMTP_PASSWORD") ?? options.SmtpPassword;
        options.SmtpFrom = Get("OAR_SMTP_FROM") ?? options.SmtpFrom;
        options.SmtpUseTls = GetBool("OAR_SMTP_USE_TLS") ?? options.SmtpUseTls;
        options.TelegramBotToken = Get("OAR_TELEGRAM_BOT_TOKEN") ?? options.TelegramBotToken;
    }

    public static void Configure(IConfigurationBuilder builder) =>
        builder.AddEnvironmentVariables(prefix: "OAR_");

    private static string? Get(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    private static bool? GetBool(string name) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : null;

    private static int? GetInt(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : null;
}
