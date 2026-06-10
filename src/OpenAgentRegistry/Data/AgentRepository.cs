using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using OpenAgentRegistry.Models;
using OpenAgentRegistry.Options;

namespace OpenAgentRegistry.Data;

public sealed class AgentRepository(IOptions<RegistryOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _connectionString = BuildConnectionString(options.Value.DatabasePath);

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(options.Value.DatabasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var connection = Open();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS agents (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                description TEXT NOT NULL DEFAULT '',
                skills_json TEXT NOT NULL DEFAULT '[]',
                seeking_json TEXT NOT NULL DEFAULT '[]',
                logical_line_id TEXT,
                contributor_lines_json TEXT NOT NULL DEFAULT '[]',
                endpoint_url TEXT,
                protocols_json TEXT NOT NULL DEFAULT '[]',
                api_key_hash TEXT NOT NULL,
                claim_token TEXT NOT NULL UNIQUE,
                claim_status TEXT NOT NULL DEFAULT 'pending_claim',
                owner_email TEXT,
                claim_code_hash TEXT,
                pending_claim_channel TEXT,
                pending_totp_secret TEXT,
                owner_totp_secret TEXT,
                owner_telegram_chat_id TEXT,
                claim_method TEXT,
                claim_step TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_agents_logical_line ON agents(logical_line_id);
            CREATE INDEX IF NOT EXISTS idx_agents_claim_status ON agents(claim_status);
            """;
        command.ExecuteNonQuery();
    }

    public bool NameExists(string name)
    {
        using var connection = Open();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM agents WHERE name = @name COLLATE NOCASE";
        command.Parameters.AddWithValue("@name", name);
        return command.ExecuteScalar() is not null;
    }

    public void Insert(AgentEntity agent)
    {
        using var connection = Open();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agents (
                id, name, description, skills_json, seeking_json, logical_line_id,
                contributor_lines_json, endpoint_url, protocols_json,
                api_key_hash, claim_token, claim_status, owner_email, claim_code_hash,
                pending_claim_channel, pending_totp_secret, owner_totp_secret,
                owner_telegram_chat_id, claim_method, claim_step, created_at, updated_at
            ) VALUES (
                @id, @name, @description, @skills, @seeking, @logical_line_id,
                @contributor_lines, @endpoint_url, @protocols,
                @api_key_hash, @claim_token, @claim_status, @owner_email, @claim_code_hash,
                @pending_claim_channel, @pending_totp_secret, @owner_totp_secret,
                @owner_telegram_chat_id, @claim_method, @claim_step, @created_at, @updated_at
            )
            """;
        BindAgent(command, agent);
        command.ExecuteNonQuery();
    }

    public AgentEntity? GetByApiKeyHash(string apiKeyHash) =>
        QuerySingle("SELECT * FROM agents WHERE api_key_hash = @hash", ("@hash", apiKeyHash));

    public AgentEntity? GetByName(string name) =>
        QuerySingle("SELECT * FROM agents WHERE name = @name COLLATE NOCASE", ("@name", name.Trim()));

    public AgentEntity? GetByClaimToken(string token) =>
        QuerySingle("SELECT * FROM agents WHERE claim_token = @token", ("@token", token));

    public IReadOnlyList<AgentEntity> Search(string? q, string? skill, string? logicalLineId, bool claimedOnly, int limit)
    {
        var clauses = new List<string> { "1=1" };
        var parameters = new List<(string, object?)>();

        if (claimedOnly)
            clauses.Add("claim_status = 'claimed'");
        if (!string.IsNullOrWhiteSpace(logicalLineId))
        {
            clauses.Add("logical_line_id = @logical_line_id");
            parameters.Add(("@logical_line_id", logicalLineId));
        }
        if (!string.IsNullOrWhiteSpace(skill))
        {
            clauses.Add("skills_json LIKE @skill");
            parameters.Add(("@skill", $"%{skill}%"));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            clauses.Add("(name LIKE @q OR description LIKE @q OR seeking_json LIKE @q)");
            parameters.Add(("@q", $"%{q}%"));
        }

        var sql = $"""
            SELECT * FROM agents
            WHERE {string.Join(" AND ", clauses)}
            ORDER BY updated_at DESC
            LIMIT @limit
            """;
        parameters.Add(("@limit", limit));
        return QueryMany(sql, parameters);
    }

    public void Update(AgentEntity agent)
    {
        using var connection = Open();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE agents SET
                description = @description,
                skills_json = @skills,
                seeking_json = @seeking,
                logical_line_id = @logical_line_id,
                contributor_lines_json = @contributor_lines,
                endpoint_url = @endpoint_url,
                protocols_json = @protocols,
                claim_status = @claim_status,
                owner_email = @owner_email,
                claim_code_hash = @claim_code_hash,
                pending_claim_channel = @pending_claim_channel,
                pending_totp_secret = @pending_totp_secret,
                owner_totp_secret = @owner_totp_secret,
                owner_telegram_chat_id = @owner_telegram_chat_id,
                claim_method = @claim_method,
                claim_step = @claim_step,
                updated_at = @updated_at
            WHERE id = @id
            """;
        BindAgent(command, agent);
        command.ExecuteNonQuery();
    }

    public static string UtcNow() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private AgentEntity? QuerySingle(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAgent(reader) : null;
    }

    private IReadOnlyList<AgentEntity> QueryMany(string sql, IReadOnlyList<(string Name, object? Value)> parameters)
    {
        using var connection = Open();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var list = new List<AgentEntity>();
        while (reader.Read())
            list.Add(ReadAgent(reader));
        return list;
    }

    private static AgentEntity ReadAgent(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        Skills = DeserializeList(reader.GetString(3)),
        Seeking = DeserializeList(reader.GetString(4)),
        LogicalLineId = reader.IsDBNull(5) ? null : reader.GetString(5),
        ContributorLines = DeserializeList(reader.GetString(6)),
        EndpointUrl = reader.IsDBNull(7) ? null : reader.GetString(7),
        Protocols = DeserializeList(reader.GetString(8)),
        ApiKeyHash = reader.GetString(9),
        ClaimToken = reader.GetString(10),
        ClaimStatus = reader.GetString(11),
        OwnerEmail = reader.IsDBNull(12) ? null : reader.GetString(12),
        ClaimCodeHash = reader.IsDBNull(13) ? null : reader.GetString(13),
        PendingClaimChannel = reader.IsDBNull(14) ? null : reader.GetString(14),
        PendingTotpSecret = reader.IsDBNull(15) ? null : reader.GetString(15),
        OwnerTotpSecret = reader.IsDBNull(16) ? null : reader.GetString(16),
        OwnerTelegramChatId = reader.IsDBNull(17) ? null : reader.GetString(17),
        ClaimMethod = reader.IsDBNull(18) ? null : reader.GetString(18),
        ClaimStep = reader.IsDBNull(19) ? null : reader.GetString(19),
        CreatedAt = reader.GetString(20),
        UpdatedAt = reader.GetString(21),
    };

    private static void BindAgent(SqliteCommand command, AgentEntity agent)
    {
        command.Parameters.AddWithValue("@id", agent.Id);
        command.Parameters.AddWithValue("@name", agent.Name);
        command.Parameters.AddWithValue("@description", agent.Description);
        command.Parameters.AddWithValue("@skills", SerializeList(agent.Skills));
        command.Parameters.AddWithValue("@seeking", SerializeList(agent.Seeking));
        command.Parameters.AddWithValue("@logical_line_id", (object?)agent.LogicalLineId ?? DBNull.Value);
        command.Parameters.AddWithValue("@contributor_lines", SerializeList(agent.ContributorLines));
        command.Parameters.AddWithValue("@endpoint_url", (object?)agent.EndpointUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@protocols", SerializeList(agent.Protocols));
        command.Parameters.AddWithValue("@api_key_hash", agent.ApiKeyHash);
        command.Parameters.AddWithValue("@claim_token", agent.ClaimToken);
        command.Parameters.AddWithValue("@claim_status", agent.ClaimStatus);
        command.Parameters.AddWithValue("@owner_email", (object?)agent.OwnerEmail ?? DBNull.Value);
        command.Parameters.AddWithValue("@claim_code_hash", (object?)agent.ClaimCodeHash ?? DBNull.Value);
        command.Parameters.AddWithValue("@pending_claim_channel", (object?)agent.PendingClaimChannel ?? DBNull.Value);
        command.Parameters.AddWithValue("@pending_totp_secret", (object?)agent.PendingTotpSecret ?? DBNull.Value);
        command.Parameters.AddWithValue("@owner_totp_secret", (object?)agent.OwnerTotpSecret ?? DBNull.Value);
        command.Parameters.AddWithValue("@owner_telegram_chat_id", (object?)agent.OwnerTelegramChatId ?? DBNull.Value);
        command.Parameters.AddWithValue("@claim_method", (object?)agent.ClaimMethod ?? DBNull.Value);
        command.Parameters.AddWithValue("@claim_step", (object?)agent.ClaimStep ?? DBNull.Value);
        command.Parameters.AddWithValue("@created_at", agent.CreatedAt);
        command.Parameters.AddWithValue("@updated_at", agent.UpdatedAt);
    }

    private static string SerializeList(IReadOnlyList<string> values) =>
        JsonSerializer.Serialize(values, JsonOptions);

    private static IReadOnlyList<string> DeserializeList(string json) =>
        JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];

    private SqliteConnection Open() => new(_connectionString);

    private static string BuildConnectionString(string path)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
        return new SqliteConnectionStringBuilder { DataSource = full }.ConnectionString;
    }
}
