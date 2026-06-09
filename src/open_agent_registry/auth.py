from __future__ import annotations

import hashlib
import re
import secrets
from uuid import uuid4

from open_agent_registry.config import settings

_NAME_RE = re.compile(r"^[a-zA-Z][a-zA-Z0-9_-]{2,63}$")


def normalize_name(name: str) -> str:
    cleaned = name.strip()
    if not _NAME_RE.match(cleaned):
        raise ValueError(
            "name must be 3–64 chars, start with a letter, use letters/digits/_/- only"
        )
    return cleaned


def new_agent_id() -> str:
    return f"agt_{uuid4().hex[:16]}"


def new_api_key() -> str:
    return f"{settings.api_key_prefix}{secrets.token_urlsafe(32)}"


def hash_secret(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def new_claim_token() -> str:
    return secrets.token_urlsafe(24)


def new_claim_code() -> str:
    return f"{secrets.randbelow(1_000_000):06d}"
