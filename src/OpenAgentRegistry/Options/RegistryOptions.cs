namespace OpenAgentRegistry.Options;

public sealed class RegistryOptions
{
    public const string SectionName = "Oar";

    public string PublicBaseUrl { get; set; } = "http://127.0.0.1:8765";
    public string DatabasePath { get; set; } = "data/registry.db";
    public bool DevExposeClaimCodes { get; set; } = true;
    public bool DevExposeTotpSecret { get; set; } = true;
    public string ApiKeyPrefix { get; set; } = "oar_";
    public bool ClaimRequire2Fa { get; set; }

    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string SmtpFrom { get; set; } = "";
    public bool SmtpUseTls { get; set; } = true;

    public string TelegramBotToken { get; set; } = "";
}
