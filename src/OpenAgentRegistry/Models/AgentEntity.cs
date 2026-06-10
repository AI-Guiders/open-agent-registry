namespace OpenAgentRegistry.Models;

public sealed record AgentEntity
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyList<string> Skills { get; init; } = [];
    public IReadOnlyList<string> Seeking { get; init; } = [];
    public string? LogicalLineId { get; init; }
    public IReadOnlyList<string> ContributorLines { get; init; } = [];
    public string? EndpointUrl { get; init; }
    public IReadOnlyList<string> Protocols { get; init; } = [];
    public required string ApiKeyHash { get; init; }
    public required string ClaimToken { get; init; }
    public string ClaimStatus { get; init; } = "pending_claim";
    public string? OwnerEmail { get; init; }
    public string? ClaimCodeHash { get; init; }
    public string? PendingClaimChannel { get; init; }
    public string? PendingTotpSecret { get; init; }
    public string? OwnerTotpSecret { get; init; }
    public string? OwnerTelegramChatId { get; init; }
    public string? ClaimMethod { get; init; }
    public string? ClaimStep { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    public bool IsClaimed => string.Equals(ClaimStatus, "claimed", StringComparison.Ordinal);
    public bool OwnerHasTotp => !string.IsNullOrEmpty(OwnerTotpSecret);
}

public sealed record AgentPublicDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyList<string> Skills { get; init; } = [];
    public IReadOnlyList<string> Seeking { get; init; } = [];
    public string? LogicalLineId { get; init; }
    public IReadOnlyList<string> ContributorLines { get; init; } = [];
    public string? EndpointUrl { get; init; }
    public IReadOnlyList<string> Protocols { get; init; } = [];
    public string ClaimStatus { get; init; } = "pending_claim";
    public string? OwnerEmail { get; init; }
    public bool OwnerHasTotp { get; init; }
    public string? ClaimMethod { get; init; }
    public bool IsClaimed { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    public static AgentPublicDto From(AgentEntity agent) => new()
    {
        Id = agent.Id,
        Name = agent.Name,
        Description = agent.Description,
        Skills = agent.Skills,
        Seeking = agent.Seeking,
        LogicalLineId = agent.LogicalLineId,
        ContributorLines = agent.ContributorLines,
        EndpointUrl = agent.EndpointUrl,
        Protocols = agent.Protocols,
        ClaimStatus = agent.ClaimStatus,
        OwnerEmail = agent.OwnerEmail,
        OwnerHasTotp = agent.OwnerHasTotp,
        ClaimMethod = agent.ClaimMethod,
        IsClaimed = agent.IsClaimed,
        CreatedAt = agent.CreatedAt,
        UpdatedAt = agent.UpdatedAt,
    };
}

public static class ClaimChannels
{
    public const string Email = "email";
    public const string Telegram = "telegram";
    public const string Totp = "totp";
    public const string StepEmailVerified = "email_verified";
}
