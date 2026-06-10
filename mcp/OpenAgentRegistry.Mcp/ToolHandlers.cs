using System.Collections.Frozen;
using System.Text.Json;

namespace OpenAgentRegistry.Mcp;

internal static class ToolHandlers
{
    internal static async Task<ApiResult> HandleAsync(
        string name,
        RegistryApiClient client,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        return name switch
        {
            "register_agent" => await client.RegisterAsync(ParseRegister(args), cancellationToken).ConfigureAwait(false),
            "search_agents" => await client.SearchAsync(ParseSearch(args), cancellationToken).ConfigureAwait(false),
            "get_agent" => await client.GetByNameAsync(RequireString(args, "name"), cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException($"Unknown tool: {name}."),
        };
    }

    private static RegisterAgentPayload ParseRegister(IReadOnlyDictionary<string, JsonElement> args)
    {
        var agentName = RequireString(args, "name");
        return new RegisterAgentPayload(
            Name: agentName,
            Description: OptionalString(args, "description"),
            Skills: OptionalStringArray(args, "skills"),
            Seeking: OptionalStringArray(args, "seeking"),
            LogicalLineId: OptionalString(args, "logical_line_id"),
            ContributorLines: OptionalStringArray(args, "contributor_lines"),
            EndpointUrl: OptionalString(args, "endpoint_url"),
            Protocols: OptionalStringArray(args, "protocols"));
    }

    private static SearchQuery ParseSearch(IReadOnlyDictionary<string, JsonElement> args)
    {
        var limit = OptionalInt(args, "limit", 20);
        limit = Math.Clamp(limit, 1, 100);
        return new SearchQuery(
            Q: OptionalString(args, "q"),
            Skill: OptionalString(args, "skill"),
            LogicalLineId: OptionalString(args, "logical_line_id"),
            ClaimedOnly: OptionalBool(args, "claimed_only", defaultValue: true),
            Limit: limit);
    }

    private static string RequireString(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        var value = OptionalString(args, key);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{key} is required.");
        return value.Trim();
    }

    private static string? OptionalString(IReadOnlyDictionary<string, JsonElement> args, string key)
        => args.TryGetValue(key, out var value) ? value.GetString() : null;

    private static bool OptionalBool(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var value))
            return defaultValue;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue,
        };
    }

    private static int OptionalInt(IReadOnlyDictionary<string, JsonElement> args, string key, int defaultValue)
        => args.TryGetValue(key, out var value) && value.TryGetInt32(out var n) ? n : defaultValue;

    private static IReadOnlyList<string>? OptionalStringArray(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                list.Add(text.Trim());
        }

        return list.Count == 0 ? null : list;
    }
}
