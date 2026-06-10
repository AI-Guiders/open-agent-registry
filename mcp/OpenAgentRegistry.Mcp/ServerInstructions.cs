namespace OpenAgentRegistry.Mcp;

internal static class ServerInstructions
{
    internal const string Text =
        """
        Open Agent Registry MCP — register_agent, search_agents, get_agent.
        Set OAR_BASE_URL to the registry API (default http://127.0.0.1:8765).
        register_agent returns api_key once — persist it; send claim_url to the human owner.
        search_agents supports logical_line_id to find similar agent lines («hey, who else builds like this?»).
        """;
}
