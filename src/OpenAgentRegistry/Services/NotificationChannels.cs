using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAgentRegistry.Options;

namespace OpenAgentRegistry.Services;

public sealed class NotificationChannels(IHttpClientFactory httpClientFactory, IOptions<RegistryOptions> options)
{
    private readonly RegistryOptions _options = options.Value;

    public bool SendEmailCode(string toEmail, string agentName, string code)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost))
            return false;

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.SmtpUseTls,
            Credentials = string.IsNullOrWhiteSpace(_options.SmtpUser)
                ? null
                : new NetworkCredential(_options.SmtpUser, _options.SmtpPassword),
        };

        var from = string.IsNullOrWhiteSpace(_options.SmtpFrom)
            ? _options.SmtpUser
            : _options.SmtpFrom;
        if (string.IsNullOrWhiteSpace(from))
            from = "noreply@open-agent-registry.local";

        using var message = new MailMessage(from, toEmail)
        {
            Subject = $"Open Agent Registry — claim code for {agentName}",
            Body = $"Your verification code for agent «{agentName}»:\n\n{code}\n\nIf you did not request this, ignore this message.",
        };
        client.Send(message);
        return true;
    }

    public async Task SendTelegramCodeAsync(string chatId, string agentName, string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.TelegramBotToken))
            throw new InvalidOperationException("Telegram bot not configured (OAR_TELEGRAM_BOT_TOKEN)");

        var text = $"Open Agent Registry\nClaim code for «{agentName}»: `{code}`\n(enter on claim page)";
        var url = $"https://api.telegram.org/bot{_options.TelegramBotToken}/sendMessage";
        var payload = new { chat_id = chatId, text, parse_mode = "Markdown" };

        var http = httpClientFactory.CreateClient();
        using var response = await http.PostAsJsonAsync(url, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!doc.RootElement.GetProperty("ok").GetBoolean())
        {
            var description = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : "Telegram API error";
            throw new InvalidOperationException(description);
        }
    }
}
