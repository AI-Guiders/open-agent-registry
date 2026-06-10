using OpenAgentRegistry.Contracts;
using OpenAgentRegistry.Data;
using OpenAgentRegistry.Models;
using OpenAgentRegistry.Options;
using OpenAgentRegistry.Security;
using Microsoft.Extensions.Options;

namespace OpenAgentRegistry.Endpoints;

public static class AgentEndpoints
{
    public static RouteGroupBuilder MapAgentApi(this RouteGroupBuilder group)
    {
        group.MapPost("/agents/register", Register);
        group.MapGet("/agents/me", GetMe).RequireAuthorization("AgentApiKey");
        group.MapGet("/agents/status", GetStatus).RequireAuthorization("AgentApiKey");
        group.MapPatch("/agents/me", UpdateMe).RequireAuthorization("AgentApiKey");
        group.MapGet("/agents/search", Search);
        group.MapGet("/agents/{name}", GetByName);
        return group;
    }

    private static IResult Register(
        RegisterAgentRequest body,
        AgentRepository repository,
        IOptions<RegistryOptions> options)
    {
        try
        {
            var name = AgentSecurity.NormalizeName(body.Name);
            if (repository.NameExists(name))
                return Results.Conflict(new { detail = $"Agent name '{name}' already taken" });

            var apiKey = AgentSecurity.NewApiKey(options.Value.ApiKeyPrefix);
            var now = AgentRepository.UtcNow();
            var agent = new AgentEntity
            {
                Id = AgentSecurity.NewAgentId(),
                Name = name,
                Description = body.Description.Trim(),
                Skills = body.Skills ?? [],
                Seeking = body.Seeking ?? [],
                LogicalLineId = body.LogicalLineId,
                ContributorLines = body.ContributorLines ?? [],
                EndpointUrl = body.EndpointUrl,
                Protocols = body.Protocols ?? [],
                ApiKeyHash = AgentSecurity.HashSecret(apiKey),
                ClaimToken = AgentSecurity.NewClaimToken(),
                CreatedAt = now,
                UpdatedAt = now,
            };
            repository.Insert(agent);

            var claimUrl = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/claim/{agent.ClaimToken}";
            return Results.Ok(new RegisterAgentResponse(agent.Id, name, apiKey, claimUrl));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
    }

    private static IResult GetMe(HttpContext context, AgentRepository repository)
    {
        var agent = GetAuthenticatedAgent(context, repository);
        return agent is null
            ? Results.Unauthorized()
            : Results.Ok(AgentPublicDto.From(agent));
    }

    private static IResult GetStatus(HttpContext context, AgentRepository repository)
    {
        var agent = GetAuthenticatedAgent(context, repository);
        return agent is null
            ? Results.Unauthorized()
            : Results.Ok(new AgentStatusResponse(agent.ClaimStatus, agent.IsClaimed, agent.OwnerEmail));
    }

    private static IResult UpdateMe(
        UpdateAgentRequest body,
        HttpContext context,
        AgentRepository repository)
    {
        var agent = GetAuthenticatedAgent(context, repository);
        if (agent is null)
            return Results.Unauthorized();

        agent = agent with
        {
            Description = body.Description?.Trim() ?? agent.Description,
            Skills = body.Skills ?? agent.Skills,
            Seeking = body.Seeking ?? agent.Seeking,
            LogicalLineId = body.LogicalLineId ?? agent.LogicalLineId,
            ContributorLines = body.ContributorLines ?? agent.ContributorLines,
            EndpointUrl = body.EndpointUrl ?? agent.EndpointUrl,
            Protocols = body.Protocols ?? agent.Protocols,
            UpdatedAt = AgentRepository.UtcNow(),
        };
        repository.Update(agent);
        return Results.Ok(AgentPublicDto.From(agent));
    }

    private static IResult Search(
        AgentRepository repository,
        string? q,
        string? skill,
        string? logical_line_id,
        bool claimed_only = true,
        int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);
        var agents = repository.Search(q, skill, logical_line_id, claimed_only, limit);
        var dtos = agents.Select(AgentPublicDto.From).ToList();
        return Results.Ok(new SearchResponse(dtos.Count, dtos));
    }

    private static IResult GetByName(string name, AgentRepository repository)
    {
        var agent = repository.GetByName(name);
        return agent is null
            ? Results.NotFound(new { detail = "Agent not found" })
            : Results.Ok(AgentPublicDto.From(agent));
    }

    private static AgentEntity? GetAuthenticatedAgent(HttpContext context, AgentRepository repository)
    {
        if (!context.Items.TryGetValue("AgentEntity", out var value) || value is not AgentEntity agent)
            return null;
        return repository.GetByApiKeyHash(agent.ApiKeyHash);
    }
}
