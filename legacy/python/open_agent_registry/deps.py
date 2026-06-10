from __future__ import annotations

from typing import Annotated

from fastapi import Depends, Header, HTTPException

from open_agent_registry.auth import hash_secret
from open_agent_registry.db import Database, row_to_agent


def get_db() -> Database:
    from open_agent_registry.app import db

    return db


def require_agent(
    authorization: Annotated[str | None, Header()] = None,
    db: Database = Depends(get_db),
) -> dict:
    if not authorization or not authorization.startswith("Bearer "):
        raise HTTPException(status_code=401, detail="Missing Bearer API key")
    api_key = authorization.removeprefix("Bearer ").strip()
    if not api_key:
        raise HTTPException(status_code=401, detail="Empty API key")
    key_hash = hash_secret(api_key)
    with db.connect() as conn:
        row = conn.execute(
            "SELECT * FROM agents WHERE api_key_hash = ?",
            (key_hash,),
        ).fetchone()
    if row is None:
        raise HTTPException(status_code=401, detail="Invalid API key")
    return row_to_agent(row)
