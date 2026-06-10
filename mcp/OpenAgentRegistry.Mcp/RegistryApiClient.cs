using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAgentRegistry.Mcp;

internal sealed class RegistryApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal async Task<ApiResult> RegisterAsync(RegisterAgentPayload payload, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/api/v1/agents/register", payload, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return await ToResultAsync(response, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ApiResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var url = BuildSearchUrl(query);
        using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        return await ToResultAsync(response, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ApiResult> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        var encoded = Uri.EscapeDataString(name.Trim());
        using var response = await http.GetAsync($"/api/v1/agents/{encoded}", cancellationToken).ConfigureAwait(false);
        return await ToResultAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildSearchUrl(SearchQuery query)
    {
        var parts = new List<string> { $"limit={query.Limit}" };
        if (!string.IsNullOrWhiteSpace(query.Q))
            parts.Add($"q={Uri.EscapeDataString(query.Q.Trim())}");
        if (!string.IsNullOrWhiteSpace(query.Skill))
            parts.Add($"skill={Uri.EscapeDataString(query.Skill.Trim())}");
        if (!string.IsNullOrWhiteSpace(query.LogicalLineId))
            parts.Add($"logical_line_id={Uri.EscapeDataString(query.LogicalLineId.Trim())}");
        if (!query.ClaimedOnly)
            parts.Add("claimed_only=false");
        return "/api/v1/agents/search?" + string.Join('&', parts);
    }

    private static async Task<ApiResult> ToResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return new ApiResult(true, string.IsNullOrWhiteSpace(body) ? "{}" : body);

        var status = (int)response.StatusCode;
        if (string.IsNullOrWhiteSpace(body))
            return new ApiResult(false, $"{{\"detail\":\"HTTP {status}\"}}");

        return new ApiResult(false, body.StartsWith('{') || body.StartsWith('[') ? body : JsonSerializer.Serialize(new { detail = body }));
    }
}

internal sealed record ApiResult(bool IsSuccess, string Body);

internal sealed record RegisterAgentPayload(
    string Name,
    string? Description = null,
    IReadOnlyList<string>? Skills = null,
    IReadOnlyList<string>? Seeking = null,
    string? LogicalLineId = null,
    IReadOnlyList<string>? ContributorLines = null,
    string? EndpointUrl = null,
    IReadOnlyList<string>? Protocols = null);

internal sealed record SearchQuery(
    string? Q = null,
    string? Skill = null,
    string? LogicalLineId = null,
    bool ClaimedOnly = true,
    int Limit = 20);
