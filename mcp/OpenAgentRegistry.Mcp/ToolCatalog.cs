using System.Text.Json;
using ModelContextProtocol.Protocol;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace OpenAgentRegistry.Mcp;

internal static class ToolCatalog
{
    private static JsonElement Schema(object schema) => JsonSerializer.SerializeToElement(schema);

    internal static List<Tool> Build() =>
    [
        new()
        {
            Name = "register_agent",
            Description =
                "Зарегистировать агента в Open Agent Registry. Возвращает api_key (один раз) и claim_url для человека. "
                + "Базовый URL: OAR_BASE_URL (по умолчанию http://127.0.0.1:8765).",
            InputSchema = Schema(new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Уникальное имя агента (slug)." },
                    description = new { type = "string", description = "Краткое описание линии / агента." },
                    skills = new { type = "array", items = new { type = "string" }, description = "Навыки / теги." },
                    seeking = new { type = "array", items = new { type = "string" }, description = "Кого или что ищет агент." },
                    logical_line_id = new { type = "string", description = "ID логической линии («найти других себя»)." },
                    contributor_lines = new { type = "array", items = new { type = "string" }, description = "Участники линии (текст)." },
                    endpoint_url = new { type = "string", description = "URL endpoint агента, если есть." },
                    protocols = new { type = "array", items = new { type = "string" }, description = "Протоколы (mcp, a2a, …)." },
                },
                required = new[] { "name" },
            }),
        },
        new()
        {
            Name = "search_agents",
            Description =
                "Поиск агентов: q (имя/описание), skill, logical_line_id. По умолчанию только claimed. "
                + "Ответ JSON: total, agents[].",
            InputSchema = Schema(new
            {
                type = "object",
                properties = new
                {
                    q = new { type = "string", description = "Поиск по имени или описанию." },
                    skill = new { type = "string", description = "Фильтр по навыку." },
                    logical_line_id = new { type = "string", description = "Фильтр по logical line." },
                    claimed_only = new { type = "boolean", description = "Только подтверждённые (по умолчанию true)." },
                    limit = new { type = "integer", description = "Максимум результатов (1–100, по умолчанию 20)." },
                },
            }),
        },
        new()
        {
            Name = "get_agent",
            Description = "Публичный профиль агента по имени.",
            InputSchema = Schema(new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Имя агента (slug)." },
                },
                required = new[] { "name" },
            }),
        },
    ];
}
