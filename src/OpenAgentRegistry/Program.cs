using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using OpenAgentRegistry.Configuration;
using OpenAgentRegistry.Data;
using OpenAgentRegistry.Endpoints;
using OpenAgentRegistry.Options;
using OpenAgentRegistry.Security;
using OpenAgentRegistry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
RegistryEnvironment.Configure(builder.Configuration);

builder.Services.Configure<RegistryOptions>(builder.Configuration.GetSection(RegistryOptions.SectionName));
builder.Services.PostConfigure<RegistryOptions>(RegistryEnvironment.ApplyEnvironmentOverrides);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<AgentRepository>();
builder.Services.AddSingleton<NotificationChannels>();
builder.Services.AddSingleton<ClaimFlowService>();

builder.Services.AddAuthentication("AgentApiKey")
    .AddScheme<AgentApiKeyAuthenticationOptions, AgentApiKeyAuthenticationHandler>("AgentApiKey", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AgentApiKey", policy =>
    {
        policy.AddAuthenticationSchemes("AgentApiKey");
        policy.RequireAuthenticatedUser();
    });
});

var app = builder.Build();

var repository = app.Services.GetRequiredService<AgentRepository>();
repository.Initialize();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "open-agent-registry",
    stack = "dotnet",
    docs = "/openapi/v1.json",
    skill = "https://github.com/AI-Guiders/open-agent-registry/blob/main/docs/skill.md",
}));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api/v1");
api.MapAgentApi();

app.MapClaimEndpoints();

app.Run();

public partial class Program;

file sealed class AgentApiKeyAuthenticationOptions : AuthenticationSchemeOptions;

file sealed class AgentApiKeyAuthenticationHandler(
    IOptionsMonitor<AgentApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder,
    AgentRepository repository)
    : AuthenticationHandler<AgentApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header))
            return Task.FromResult(AuthenticateResult.NoResult());

        var value = header.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("Missing Bearer API key"));

        var apiKey = value["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(apiKey))
            return Task.FromResult(AuthenticateResult.Fail("Empty API key"));

        var agent = repository.GetByApiKeyHash(AgentSecurity.HashSecret(apiKey));
        if (agent is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

        Context.Items["AgentEntity"] = agent;
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, agent.Name)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
