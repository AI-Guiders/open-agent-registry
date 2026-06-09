from __future__ import annotations

import json
import sqlite3
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterator

from open_agent_registry.config import settings


def _utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat()


def _ensure_parent(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)


class Database:
    def __init__(self, path: str | None = None) -> None:
        self.path = Path(path or settings.database_path)
        _ensure_parent(self.path)
        self._init_schema()

    @contextmanager
    def connect(self) -> Iterator[sqlite3.Connection]:
        conn = sqlite3.connect(self.path)
        conn.row_factory = sqlite3.Row
        try:
            yield conn
            conn.commit()
        finally:
            conn.close()

    def _init_schema(self) -> None:
        with self.connect() as conn:
            conn.executescript(
                """
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
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_agents_logical_line
                    ON agents(logical_line_id);
                CREATE INDEX IF NOT EXISTS idx_agents_claim_status
                    ON agents(claim_status);
                """
            )


def row_to_agent(row: sqlite3.Row) -> dict[str, Any]:
    return {
        "id": row["id"],
        "name": row["name"],
        "description": row["description"],
        "skills": json.loads(row["skills_json"]),
        "seeking": json.loads(row["seeking_json"]),
        "logical_line_id": row["logical_line_id"],
        "contributor_lines": json.loads(row["contributor_lines_json"]),
        "endpoint_url": row["endpoint_url"],
        "protocols": json.loads(row["protocols_json"]),
        "claim_status": row["claim_status"],
        "owner_email": row["owner_email"],
        "is_claimed": row["claim_status"] == "claimed",
        "created_at": row["created_at"],
        "updated_at": row["updated_at"],
    }


def public_agent(agent: dict[str, Any]) -> dict[str, Any]:
    return {k: v for k, v in agent.items() if k not in {"api_key_hash", "claim_token"}}
