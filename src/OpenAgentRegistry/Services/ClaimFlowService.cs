using Microsoft.Extensions.Options;
using OpenAgentRegistry.Data;
using OpenAgentRegistry.Models;
using OpenAgentRegistry.Options;
using OpenAgentRegistry.Security;
using OpenAgentRegistry.Services;

namespace OpenAgentRegistry.Services;

public sealed class ClaimFlowService(
    AgentRepository repository,
    NotificationChannels notifications,
    IOptions<RegistryOptions> options)
{
    private readonly RegistryOptions _options = options.Value;

    public async Task<Dictionary<string, string>> BeginAsync(
        string token,
        string email,
        string channel,
        string? telegramChatId,
        CancellationToken cancellationToken)
    {
        var ownerEmail = AgentSecurity.NormalizeEmail(email);
        channel = channel.Trim().ToLowerInvariant();
        if (channel is not (ClaimChannels.Email or ClaimChannels.Telegram or ClaimChannels.Totp))
            throw new RegistryApiException(400, "channel must be email, telegram, or totp");

        var agent = RequirePending(token);
        var now = AgentRepository.UtcNow();

        if (channel == ClaimChannels.Totp)
        {
            var secret = TotpHelper.NewSecret();
            agent = agent with
            {
                OwnerEmail = ownerEmail,
                PendingClaimChannel = ClaimChannels.Totp,
                PendingTotpSecret = secret,
                ClaimCodeHash = null,
                ClaimStep = null,
                OwnerTelegramChatId = telegramChatId,
                UpdatedAt = now,
            };
            repository.Update(agent);

            var payload = new Dictionary<string, string>
            {
                ["channel"] = ClaimChannels.Totp,
                ["message"] = "Scan otpauth_uri in your authenticator app, then confirm with a 6-digit code.",
                ["otpauth_uri"] = TotpHelper.BuildOtpAuthUri(secret, $"{agent.Name}:{ownerEmail}"),
                ["email"] = ownerEmail,
            };
            if (_options.DevExposeTotpSecret)
            {
                payload["dev_totp_secret"] = secret;
                payload["note"] = "dev_totp_secret only when OAR_DEV_EXPOSE_TOTP_SECRET=true";
            }
            return payload;
        }

        if (channel == ClaimChannels.Telegram)
        {
            if (string.IsNullOrWhiteSpace(telegramChatId))
                throw new RegistryApiException(400, "telegram_chat_id required for telegram channel");
            if (string.IsNullOrWhiteSpace(_options.TelegramBotToken))
                throw new RegistryApiException(503, "Telegram bot not configured (OAR_TELEGRAM_BOT_TOKEN)");
        }

        var code = AgentSecurity.NewClaimCode();
        agent = agent with
        {
            OwnerEmail = ownerEmail,
            PendingClaimChannel = channel,
            PendingTotpSecret = null,
            ClaimCodeHash = AgentSecurity.HashSecret(code),
            ClaimStep = null,
            OwnerTelegramChatId = channel == ClaimChannels.Telegram ? telegramChatId : null,
            UpdatedAt = now,
        };
        repository.Update(agent);

        var result = new Dictionary<string, string>
        {
            ["channel"] = channel,
            ["message"] = "Verification code issued.",
            ["email"] = ownerEmail,
        };

        if (channel == ClaimChannels.Email)
        {
            if (notifications.SendEmailCode(ownerEmail, agent.Name, code))
                result["delivery"] = "smtp";
            else if (_options.DevExposeClaimCodes)
            {
                result["dev_code"] = code;
                result["delivery"] = "dev_json";
                result["note"] = "Configure OAR_SMTP_* for email delivery; dev_code when OAR_DEV_EXPOSE_CLAIM_CODES=true";
            }
            else
                throw new RegistryApiException(503, "SMTP not configured and dev codes disabled");
        }
        else
        {
            await notifications.SendTelegramCodeAsync(telegramChatId!, agent.Name, code, cancellationToken);
            result["delivery"] = "telegram";
            if (_options.DevExposeClaimCodes)
                result["dev_code"] = code;
        }

        return result;
    }

    public async Task<Dictionary<string, string>> Begin2FaAsync(string token, string email, CancellationToken cancellationToken)
    {
        var ownerEmail = AgentSecurity.NormalizeEmail(email);
        var agent = RequirePending(token);
        var code = AgentSecurity.NewClaimCode();
        agent = agent with
        {
            OwnerEmail = ownerEmail,
            PendingClaimChannel = ClaimChannels.Email,
            PendingTotpSecret = null,
            ClaimCodeHash = AgentSecurity.HashSecret(code),
            ClaimStep = null,
            UpdatedAt = AgentRepository.UtcNow(),
        };
        repository.Update(agent);

        var payload = new Dictionary<string, string>
        {
            ["mode"] = "2fa",
            ["step"] = "1",
            ["next"] = "confirm-email then setup-totp",
            ["email"] = ownerEmail,
        };

        if (notifications.SendEmailCode(ownerEmail, agent.Name, code))
            payload["delivery"] = "smtp";
        else if (_options.DevExposeClaimCodes)
        {
            payload["dev_code"] = code;
            payload["delivery"] = "dev_json";
        }
        else
            throw new RegistryApiException(503, "SMTP not configured and dev codes disabled");

        await Task.CompletedTask;
        return payload;
    }

    public Dictionary<string, string> SetupTotp2Fa(string token)
    {
        var agent = RequirePending(token);
        if (!string.Equals(agent.ClaimStep, ClaimChannels.StepEmailVerified, StringComparison.Ordinal))
            throw new RegistryApiException(400, "Complete email verification first (POST .../confirm-email)");

        var secret = TotpHelper.NewSecret();
        var ownerEmail = agent.OwnerEmail ?? "owner";
        agent = agent with
        {
            PendingTotpSecret = secret,
            PendingClaimChannel = ClaimChannels.Totp,
            UpdatedAt = AgentRepository.UtcNow(),
        };
        repository.Update(agent);

        var payload = new Dictionary<string, string>
        {
            ["mode"] = "2fa",
            ["step"] = "2",
            ["message"] = "Scan otpauth_uri, then POST .../confirm-totp",
            ["otpauth_uri"] = TotpHelper.BuildOtpAuthUri(secret, $"{agent.Name}:{ownerEmail}"),
        };
        if (_options.DevExposeTotpSecret)
            payload["dev_totp_secret"] = secret;
        return payload;
    }

    public Dictionary<string, string> ConfirmAsync(string token, string email, string code)
    {
        var ownerEmail = AgentSecurity.NormalizeEmail(email);
        var agent = RequirePending(token);
        if (!string.Equals(agent.OwnerEmail, ownerEmail, StringComparison.OrdinalIgnoreCase))
            throw new RegistryApiException(400, "Email does not match pending claim");

        var channel = agent.PendingClaimChannel ?? ClaimChannels.Email;
        if (channel == ClaimChannels.Totp)
        {
            if (string.IsNullOrEmpty(agent.PendingTotpSecret) || !TotpHelper.Verify(agent.PendingTotpSecret, code))
                throw new RegistryApiException(400, "Invalid authenticator code");

            agent = agent with
            {
                ClaimStatus = "claimed",
                OwnerTotpSecret = agent.PendingTotpSecret,
                PendingTotpSecret = null,
                ClaimCodeHash = null,
                ClaimMethod = ClaimChannels.Totp,
                UpdatedAt = AgentRepository.UtcNow(),
            };
            repository.Update(agent);
            return new Dictionary<string, string>
            {
                ["status"] = "claimed",
                ["message"] = "Agent claimed with authenticator.",
                ["owner_totp_enabled"] = "true",
            };
        }

        if (AgentSecurity.HashSecret(code.Trim()) != agent.ClaimCodeHash)
            throw new RegistryApiException(400, "Invalid verification code");

        var method = channel is ClaimChannels.Email or ClaimChannels.Telegram ? channel : ClaimChannels.Email;
        agent = agent with
        {
            ClaimStatus = "claimed",
            ClaimCodeHash = null,
            ClaimMethod = method,
            UpdatedAt = AgentRepository.UtcNow(),
        };
        repository.Update(agent);
        return new Dictionary<string, string>
        {
            ["status"] = "claimed",
            ["message"] = $"Agent claimed via {method}.",
        };
    }

    public Dictionary<string, string> ConfirmEmailStepAsync(string token, string email, string code)
    {
        if (!_options.ClaimRequire2Fa)
            return ConfirmAsync(token, email, code);

        var ownerEmail = AgentSecurity.NormalizeEmail(email);
        var agent = RequirePending(token);
        if (!string.Equals(agent.OwnerEmail, ownerEmail, StringComparison.OrdinalIgnoreCase))
            throw new RegistryApiException(400, "Email does not match pending claim");
        if (AgentSecurity.HashSecret(code.Trim()) != agent.ClaimCodeHash)
            throw new RegistryApiException(400, "Invalid verification code");

        agent = agent with
        {
            ClaimStep = ClaimChannels.StepEmailVerified,
            ClaimCodeHash = null,
            UpdatedAt = AgentRepository.UtcNow(),
        };
        repository.Update(agent);
        return new Dictionary<string, string>
        {
            ["status"] = "email_verified",
            ["next"] = $"POST /claim/{token}/setup-totp",
        };
    }

    public Dictionary<string, string> ConfirmTotpAsync(string token, string email, string code)
    {
        var ownerEmail = AgentSecurity.NormalizeEmail(email);
        var agent = RequirePending(token);
        if (!string.Equals(agent.OwnerEmail, ownerEmail, StringComparison.OrdinalIgnoreCase))
            throw new RegistryApiException(400, "Email does not match pending claim");
        if (string.IsNullOrEmpty(agent.PendingTotpSecret))
            throw new RegistryApiException(400, "TOTP not started; POST .../begin with channel=totp");
        if (!TotpHelper.Verify(agent.PendingTotpSecret, code))
            throw new RegistryApiException(400, "Invalid authenticator code");

        var twoFa = _options.ClaimRequire2Fa ||
                    string.Equals(agent.ClaimStep, ClaimChannels.StepEmailVerified, StringComparison.Ordinal);
        var method = twoFa ? "2fa" : ClaimChannels.Totp;
        agent = agent with
        {
            ClaimStatus = "claimed",
            ClaimCodeHash = null,
            PendingTotpSecret = null,
            OwnerTotpSecret = agent.PendingTotpSecret,
            ClaimStep = null,
            PendingClaimChannel = ClaimChannels.Totp,
            ClaimMethod = method,
            UpdatedAt = AgentRepository.UtcNow(),
        };
        repository.Update(agent);
        return new Dictionary<string, string>
        {
            ["status"] = "claimed",
            ["message"] = "Agent claimed with authenticator.",
            ["owner_totp_enabled"] = "true",
            ["claim_method"] = method,
        };
    }

    private AgentEntity RequirePending(string token)
    {
        var agent = repository.GetByClaimToken(token)
            ?? throw new RegistryApiException(404, "Invalid claim token");
        if (agent.IsClaimed)
            throw new RegistryApiException(409, "Already claimed");
        return agent;
    }
}
