using System.Collections.Frozen;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenAgentRegistry.Mcp;

const string DefaultBaseUrl = "http://127.0.0.1:8765";

var baseUrl = Environment.GetEnvironmentVariable("OAR_BASE_URL")?.Trim();
if (string.IsNullOrWhiteSpace(baseUrl))
    baseUrl = DefaultBaseUrl;

if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
{
    Console.Error.WriteLine("OAR_BASE_URL must be an absolute URL.");
    return 1;
}

using var http = new HttpClient { BaseAddress = baseUri };
http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

var client = new RegistryApiClient(http);
var toolsList = ToolCatalog.Build();

var options = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "OpenAgentRegistryMcp", Version = "0.1.0" },
    ServerInstructions = ServerInstructions.Text,
    ProtocolVersion = "2024-11-05",
    Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = false } },
    Handlers = new McpServerHandlers
    {
        ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = toolsList }),
        CallToolHandler = async (request, cancellationToken) =>
        {
            var name = request.Params?.Name ?? "";
            var args = request.Params?.Arguments is IReadOnlyDictionary<string, JsonElement> dictionary
                ? dictionary
                : FrozenDictionary<string, JsonElement>.Empty;

            try
            {
                var result = await ToolHandlers.HandleAsync(name, client, args, cancellationToken).ConfigureAwait(false);
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = result.Body }],
                    IsError = !result.IsSuccess,
                };
            }
            catch (ArgumentException ex)
            {
                return Error(ex.Message);
            }
            catch (HttpRequestException ex)
            {
                return Error($"Registry unreachable at {baseUri}: {ex.Message}");
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        },
    },
};

static CallToolResult Error(string message) =>
    new()
    {
        Content = [new TextContentBlock { Text = $"Error: {message}" }],
        IsError = true,
    };

var transport = new StdioServerTransport("OpenAgentRegistryMcp");
await using var server = McpServer.Create(transport, options);
await server.RunAsync();
return 0;
