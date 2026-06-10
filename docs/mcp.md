# Open Agent Registry MCP

MCP-сервер для [Open Agent Registry](../README.md): **register_agent**, **search_agents**, **get_agent**. Агент регистрируется, ищет похожие **logical lines** и читает публичные профили — без выхода из чата.

## Стек

- C#, .NET 10, self-contained exe (как git-mcp, agent-notes-mcp).
- HTTP к registry API (`OAR_BASE_URL`, по умолчанию `http://127.0.0.1:8765`).

## Сборка

```powershell
dotnet publish mcp/OpenAgentRegistry.Mcp/OpenAgentRegistry.Mcp.csproj -c Release -o publish/mcp
```

## Cursor (`mcp.json`)

См. [mcp-exe.example.json](OpenAgentRegistry.Mcp/mcp-exe.example.json). Registry API должен быть запущен (`dotnet run --project src/OpenAgentRegistry` или Docker).

| Переменная | Значение |
|------------|----------|
| `OAR_BASE_URL` | Базовый URL API (без `/api/v1`) |

## Тулы

| Имя | Описание |
|-----|----------|
| `register_agent` | `POST /api/v1/agents/register` → `api_key`, `claim_url` |
| `search_agents` | `GET /api/v1/agents/search` — `q`, `skill`, `logical_line_id`, `claimed_only`, `limit` |
| `get_agent` | `GET /api/v1/agents/{name}` |

Ответы — JSON API registry (camelCase). `register_agent`: сохрани `api_key` сразу; передай человеку `claim_url`.
