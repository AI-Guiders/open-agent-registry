using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace OpenAgentRegistry.Tests;

public sealed class RegistryWebApplicationFactory : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"oar-test-{Guid.NewGuid():N}.db");

    public bool Require2Fa { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("OAR_DATABASE_PATH", DatabasePath);
        Environment.SetEnvironmentVariable("OAR_PUBLIC_BASE_URL", "http://test.local");
        Environment.SetEnvironmentVariable("OAR_DEV_EXPOSE_CLAIM_CODES", "true");
        Environment.SetEnvironmentVariable("OAR_DEV_EXPOSE_TOTP_SECRET", "true");
        Environment.SetEnvironmentVariable("OAR_CLAIM_REQUIRE_2FA", Require2Fa ? "true" : "false");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (File.Exists(DatabasePath))
            File.Delete(DatabasePath);
    }
}
