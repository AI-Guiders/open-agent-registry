using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OtpNet;

namespace OpenAgentRegistry.Tests;

public sealed class RegistryApiTests(RegistryWebApplicationFactory factory) : IClassFixture<RegistryWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Register_search_and_claim_via_email()
    {
        var reg = await _client.PostAsJsonAsync("/api/v1/agents/register", new
        {
            name = "ComposerCasa",
            description = "CASA lab line",
            skills = new[] { "casa", "python" },
            logical_line_id = "composer-cursor-2026",
            contributor_lines = new[] { "Composer @ Cursor, 2026-06-09" },
        });
        reg.EnsureSuccessStatusCode();
        var regBody = await reg.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var apiKey = regBody.GetProperty("apiKey").GetString()!;
        var claimUrl = regBody.GetProperty("claimUrl").GetString()!;
        var token = claimUrl.Split('/')[^1];

        var searchBefore = await _client.GetFromJsonAsync<JsonElement>("/api/v1/agents/search?q=Composer");
        Assert.Equal(0, searchBefore.GetProperty("total").GetInt32());

        var codeResp = await _client.PostAsJsonAsync($"/claim/{token}/request-code", new { email = "owner@example.com" });
        codeResp.EnsureSuccessStatusCode();
        var codeBody = await codeResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var code = codeBody.TryGetProperty("devCode", out var devCode)
            ? devCode.GetString()!
            : codeBody.GetProperty("dev_code").GetString()!;

        var confirm = await _client.PostAsJsonAsync($"/claim/{token}/confirm", new { email = "owner@example.com", code });
        confirm.EnsureSuccessStatusCode();
        Assert.Equal("claimed", (await confirm.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("status").GetString());

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/agents/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var me = await _client.SendAsync(meRequest);
        me.EnsureSuccessStatusCode();
        var meBody = await me.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(meBody.GetProperty("isClaimed").GetBoolean());

        var searchAfter = await _client.GetFromJsonAsync<JsonElement>("/api/v1/agents/search?q=CASA");
        Assert.Equal(1, searchAfter.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Claim_via_totp()
    {
        var reg = await _client.PostAsJsonAsync("/api/v1/agents/register", new { name = "TotpLine", description = "TOTP" });
        reg.EnsureSuccessStatusCode();
        var regBody = await reg.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = regBody.GetProperty("claimUrl").GetString()!.Split('/')[^1];

        var begin = await _client.PostAsJsonAsync($"/claim/{token}/begin", new { email = "totp@example.com", channel = "totp" });
        begin.EnsureSuccessStatusCode();
        var beginBody = await begin.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var secret = beginBody.TryGetProperty("devTotpSecret", out var devSecret)
            ? devSecret.GetString()!
            : beginBody.GetProperty("dev_totp_secret").GetString()!;
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

        var confirm = await _client.PostAsJsonAsync($"/claim/{token}/confirm", new { email = "totp@example.com", code });
        confirm.EnsureSuccessStatusCode();

        var profile = await _client.GetFromJsonAsync<JsonElement>("/api/v1/agents/TotpLine");
        Assert.True(profile.GetProperty("ownerHasTotp").GetBoolean());
        Assert.Equal("totp", profile.GetProperty("claimMethod").GetString());
    }

    [Fact]
    public async Task Claim_2fa_flow()
    {
        await using var factory2fa = new RegistryWebApplicationFactory { Require2Fa = true };
        var client = factory2fa.CreateClient();

        var reg = await client.PostAsJsonAsync("/api/v1/agents/register", new { name = "TwoFaLine", description = "2FA" });
        reg.EnsureSuccessStatusCode();
        var token = (await reg.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("claimUrl").GetString()!.Split('/')[^1];

        var begin = await client.PostAsJsonAsync($"/claim/{token}/begin-2fa", new { email = "2fa@example.com" });
        begin.EnsureSuccessStatusCode();
        var beginBody = await begin.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var emailCode = GetDevCode(beginBody);

        var step1 = await client.PostAsJsonAsync($"/claim/{token}/confirm-email", new { email = "2fa@example.com", code = emailCode });
        step1.EnsureSuccessStatusCode();
        Assert.Equal("email_verified", (await step1.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("status").GetString());

        var setup = await client.PostAsync($"/claim/{token}/setup-totp", null);
        setup.EnsureSuccessStatusCode();
        var setupBody = await setup.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var secret = setupBody.TryGetProperty("devTotpSecret", out var devSecret)
            ? devSecret.GetString()!
            : setupBody.GetProperty("dev_totp_secret").GetString()!;
        var totpCode = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

        var step2 = await client.PostAsJsonAsync($"/claim/{token}/confirm-totp", new { email = "2fa@example.com", code = totpCode });
        step2.EnsureSuccessStatusCode();
        var step2Body = await step2.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var method = step2Body.TryGetProperty("claimMethod", out var cm) ? cm.GetString() : step2Body.GetProperty("claim_method").GetString();
        Assert.Equal("2fa", method);
    }

    private static string GetDevCode(JsonElement body) =>
        body.TryGetProperty("devCode", out var devCode) ? devCode.GetString()! : body.GetProperty("dev_code").GetString()!;
}
