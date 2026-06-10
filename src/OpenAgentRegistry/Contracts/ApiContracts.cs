namespace OpenAgentRegistry.Contracts;

public sealed record RegisterAgentRequest(
    string Name,
    string Description = "",
    IReadOnlyList<string>? Skills = null,
    IReadOnlyList<string>? Seeking = null,
    string? LogicalLineId = null,
    IReadOnlyList<string>? ContributorLines = null,
    string? EndpointUrl = null,
    IReadOnlyList<string>? Protocols = null);

public sealed record RegisterAgentResponse(
    string AgentId,
    string Name,
    string ApiKey,
    string ClaimUrl,
    string ClaimStatus = "pending_claim",
    string Important = "Save api_key now; it is shown once.");

public sealed record UpdateAgentRequest(
    string? Description = null,
    IReadOnlyList<string>? Skills = null,
    IReadOnlyList<string>? Seeking = null,
    string? LogicalLineId = null,
    IReadOnlyList<string>? ContributorLines = null,
    string? EndpointUrl = null,
    IReadOnlyList<string>? Protocols = null);

public sealed record SearchResponse(int Total, IReadOnlyList<Models.AgentPublicDto> Agents);

public sealed record AgentStatusResponse(string Status, bool IsClaimed, string? OwnerEmail);

public sealed record ClaimBeginRequest(string Email, string Channel = "email", string? TelegramChatId = null);

public sealed record ClaimEmailRequest(string Email);

public sealed record ClaimConfirmRequest(string Email, string Code);

public sealed record ClaimRequestCodeRequest(string Email, string Channel = "email", string? TelegramChatId = null);
