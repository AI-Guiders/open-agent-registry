using System.Collections.Frozen;
using System.Text.Json;
using OpenAgentRegistry.Mcp;
using OpenAgentRegistry.Tests;

namespace OpenAgentRegistry.Mcp.Tests;

public sealed class McpToolTests(RegistryWebApplicationFactory factory) : IClassFixture<RegistryWebApplicationFactory>
{
    [Fact]
    public async Task Register_and_search_via_mcp_handlers()
    {
        using var http = factory.CreateClient();
        var client = new RegistryApiClient(http);

        var registerArgs = FrozenDictionary<string, JsonElement>.Empty
            .AppendPair("name", JsonSerializer.SerializeToElement("McpLine"))
            .AppendPair("description", JsonSerializer.SerializeToElement("MCP integration test"))
            .AppendPair("logical_line_id", JsonSerializer.SerializeToElement("line-mcp-test"));

        var register = await ToolHandlers.HandleAsync("register_agent", client, registerArgs, CancellationToken.None);
        Assert.True(register.IsSuccess);
        using var regDoc = JsonDocument.Parse(register.Body);
        Assert.True(regDoc.RootElement.TryGetProperty("apiKey", out var apiKey));
        Assert.StartsWith("oar_", apiKey.GetString());

        var searchArgs = FrozenDictionary<string, JsonElement>.Empty
            .AppendPair("logical_line_id", JsonSerializer.SerializeToElement("line-mcp-test"))
            .AppendPair("claimed_only", JsonSerializer.SerializeToElement(false));

        var search = await ToolHandlers.HandleAsync("search_agents", client, searchArgs, CancellationToken.None);
        Assert.True(search.IsSuccess);
        using var searchDoc = JsonDocument.Parse(search.Body);
        Assert.Equal(1, searchDoc.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Get_agent_returns_not_found_for_missing()
    {
        using var http = factory.CreateClient();
        var client = new RegistryApiClient(http);
        var args = FrozenDictionary<string, JsonElement>.Empty
            .AppendPair("name", JsonSerializer.SerializeToElement("no-such-agent"));

        var result = await ToolHandlers.HandleAsync("get_agent", client, args, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Body, StringComparison.OrdinalIgnoreCase);
    }
}

file static class FrozenDictionaryExtensions
{
    internal static FrozenDictionary<string, JsonElement> AppendPair(
        this FrozenDictionary<string, JsonElement> source,
        string key,
        JsonElement value)
    {
        var dict = source.ToDictionary(static pair => pair.Key, static pair => pair.Value);
        dict[key] = value;
        return dict.ToFrozenDictionary();
    }
}
