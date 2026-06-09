from __future__ import annotations

import json
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query
from fastapi.responses import HTMLResponse

from open_agent_registry.auth import (
    hash_secret,
    new_agent_id,
    new_api_key,
    new_claim_token,
    normalize_name,
)
from open_agent_registry.config import settings
from open_agent_registry.db import Database, _utc_now, public_agent, row_to_agent
from open_agent_registry.deps import get_db, require_agent
from open_agent_registry.claim_flow import (
    begin_claim,
    begin_claim_2fa,
    confirm_claim,
    confirm_email_step,
    confirm_totp,
    setup_totp_2fa,
)
from open_agent_registry.schemas import (
    AgentPublic,
    AgentStatusResponse,
    ClaimBeginBody,
    ClaimConfirmBody,
    ClaimEmailOnlyBody,
    ClaimRequestCodeBody,
    RegisterAgentRequest,
    RegisterAgentResponse,
    SearchResponse,
    UpdateAgentRequest,
)

router = APIRouter(prefix="/api/v1")


@router.post("/agents/register", response_model=RegisterAgentResponse)
def register_agent(body: RegisterAgentRequest, db: Database = Depends(get_db)) -> RegisterAgentResponse:
    try:
        name = normalize_name(body.name)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    api_key = new_api_key()
    claim_token = new_claim_token()
    agent_id = new_agent_id()
    now = _utc_now()

    with db.connect() as conn:
        exists = conn.execute("SELECT 1 FROM agents WHERE name = ? COLLATE NOCASE", (name,)).fetchone()
        if exists:
            raise HTTPException(status_code=409, detail=f"Agent name '{name}' already taken")
        conn.execute(
            """
            INSERT INTO agents (
                id, name, description, skills_json, seeking_json, logical_line_id,
                contributor_lines_json, endpoint_url, protocols_json,
                api_key_hash, claim_token, claim_status, owner_email, claim_code_hash,
                created_at, updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'pending_claim', NULL, NULL, ?, ?)
            """,
            (
                agent_id,
                name,
                body.description.strip(),
                json.dumps(body.skills),
                json.dumps(body.seeking),
                body.logical_line_id,
                json.dumps(body.contributor_lines),
                body.endpoint_url,
                json.dumps(body.protocols),
                hash_secret(api_key),
                claim_token,
                now,
                now,
            ),
        )

    claim_url = f"{settings.public_base_url.rstrip('/')}/claim/{claim_token}"
    return RegisterAgentResponse(
        agent_id=agent_id,
        name=name,
        api_key=api_key,
        claim_url=claim_url,
    )


@router.get("/agents/me", response_model=AgentPublic)
def get_me(agent: dict = Depends(require_agent)) -> AgentPublic:
    return AgentPublic(**public_agent(agent))


@router.get("/agents/status", response_model=AgentStatusResponse)
def get_status(agent: dict = Depends(require_agent)) -> AgentStatusResponse:
    return AgentStatusResponse(
        status=agent["claim_status"],
        is_claimed=agent["is_claimed"],
        owner_email=agent["owner_email"],
    )


@router.patch("/agents/me", response_model=AgentPublic)
def update_me(
    body: UpdateAgentRequest,
    agent: dict = Depends(require_agent),
    db: Database = Depends(get_db),
) -> AgentPublic:
    fields: dict[str, Any] = {}
    if body.description is not None:
        fields["description"] = body.description.strip()
    if body.skills is not None:
        fields["skills_json"] = json.dumps(body.skills)
    if body.seeking is not None:
        fields["seeking_json"] = json.dumps(body.seeking)
    if body.logical_line_id is not None:
        fields["logical_line_id"] = body.logical_line_id or None
    if body.contributor_lines is not None:
        fields["contributor_lines_json"] = json.dumps(body.contributor_lines)
    if body.endpoint_url is not None:
        fields["endpoint_url"] = body.endpoint_url or None
    if body.protocols is not None:
        fields["protocols_json"] = json.dumps(body.protocols)
    if not fields:
        return AgentPublic(**public_agent(agent))

    fields["updated_at"] = _utc_now()
    set_clause = ", ".join(f"{column} = ?" for column in fields)
    values = list(fields.values()) + [agent["id"]]

    with db.connect() as conn:
        conn.execute(f"UPDATE agents SET {set_clause} WHERE id = ?", values)
        row = conn.execute("SELECT * FROM agents WHERE id = ?", (agent["id"],)).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="Agent not found")
    return AgentPublic(**public_agent(row_to_agent(row)))


@router.get("/agents/search", response_model=SearchResponse)
def search_agents(
    q: str | None = Query(default=None, max_length=200),
    skill: str | None = Query(default=None, max_length=100),
    logical_line_id: str | None = Query(default=None, max_length=128),
    claimed_only: bool = Query(default=True),
    limit: int = Query(default=20, ge=1, le=100),
    db: Database = Depends(get_db),
) -> SearchResponse:
    clauses = ["1=1"]
    params: list[Any] = []
    if claimed_only:
        clauses.append("claim_status = 'claimed'")
    if logical_line_id:
        clauses.append("logical_line_id = ?")
        params.append(logical_line_id)
    if skill:
        clauses.append("skills_json LIKE ?")
        params.append(f"%{skill}%")
    if q:
        clauses.append("(name LIKE ? OR description LIKE ? OR seeking_json LIKE ?)")
        like = f"%{q}%"
        params.extend([like, like, like])

    sql = f"""
        SELECT * FROM agents
        WHERE {' AND '.join(clauses)}
        ORDER BY updated_at DESC
        LIMIT ?
    """
    params.append(limit)

    with db.connect() as conn:
        rows = conn.execute(sql, params).fetchall()

    agents = [AgentPublic(**public_agent(row_to_agent(row))) for row in rows]
    return SearchResponse(total=len(agents), agents=agents)


@router.get("/agents/{name}", response_model=AgentPublic)
def get_agent_by_name(name: str, db: Database = Depends(get_db)) -> AgentPublic:
    with db.connect() as conn:
        row = conn.execute(
            "SELECT * FROM agents WHERE name = ? COLLATE NOCASE",
            (name.strip(),),
        ).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="Agent not found")
    return AgentPublic(**public_agent(row_to_agent(row)))


claim_router = APIRouter()


@claim_router.get("/claim/{token}", response_class=HTMLResponse)
def claim_page(token: str, db: Database = Depends(get_db)) -> HTMLResponse:
    with db.connect() as conn:
        row = conn.execute("SELECT name, claim_status FROM agents WHERE claim_token = ?", (token,)).fetchone()
    if row is None:
        return HTMLResponse("<h1>Invalid claim link</h1>", status_code=404)
    status = row["claim_status"]
    name = row["name"]
    if status == "claimed":
        body = f"<p>Agent <strong>{name}</strong> is already claimed.</p>"
    else:
        body = f"""
        <h1>Claim agent: {name}</h1>
        <p>No X/Twitter. Pick a verification channel:</p>
        <ul>
          <li><b>totp</b> — Google Authenticator / Aegis / any TOTP app</li>
          <li><b>email</b> — one-time code (SMTP or dev JSON)</li>
          <li><b>telegram</b> — code via bot (needs chat_id + OAR_TELEGRAM_BOT_TOKEN)</li>
          <li><b>2fa</b> — email then authenticator (<code>OAR_CLAIM_REQUIRE_2FA=true</code>)</li>
        </ul>
        <p>API: POST <code>/claim/{token}/begin</code> → POST <code>/claim/{token}/confirm</code></p>
        <label>Email <input id="email" type="email" /></label>
        <label>Channel
          <select id="channel">
            <option value="email">email</option>
            <option value="totp">totp (authenticator)</option>
            <option value="telegram">telegram</option>
          </select>
        </label>
        <label>Telegram chat_id <input id="tg" placeholder="optional" /></label>
        <button onclick="beginClaim()">Begin</button>
        <label>Code <input id="code" /></label>
        <button onclick="confirmClaim()">Confirm</button>
        <pre id="out"></pre>
        <script>
        const out = document.getElementById('out');
        async function beginClaim() {{
          const email = document.getElementById('email').value;
          const channel = document.getElementById('channel').value;
          const telegram_chat_id = document.getElementById('tg').value || null;
          const r = await fetch('/claim/{token}/begin', {{
            method:'POST', headers:{{'Content-Type':'application/json'}},
            body: JSON.stringify({{email, channel, telegram_chat_id}})
          }});
          out.textContent = JSON.stringify(await r.json(), null, 2);
        }}
        async function confirmClaim() {{
          const email = document.getElementById('email').value;
          const code = document.getElementById('code').value;
          const r = await fetch('/claim/{token}/confirm', {{
            method:'POST', headers:{{'Content-Type':'application/json'}},
            body: JSON.stringify({{email, code}})
          }});
          out.textContent = JSON.stringify(await r.json(), null, 2);
        }}
        </script>
        """
    html = f"<!DOCTYPE html><html><head><meta charset='utf-8'><title>Claim {name}</title></head><body>{body}</body></html>"
    return HTMLResponse(html)


@claim_router.post("/claim/{token}/begin")
def claim_begin(token: str, body: ClaimBeginBody, db: Database = Depends(get_db)) -> dict[str, str]:
    try:
        return begin_claim(
            db,
            token,
            email=body.email,
            channel=body.channel,
            telegram_chat_id=body.telegram_chat_id,
        )
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@claim_router.post("/claim/{token}/begin-2fa")
def claim_begin_two_factor(token: str, body: ClaimEmailOnlyBody, db: Database = Depends(get_db)) -> dict[str, str]:
    try:
        return begin_claim_2fa(db, token, email=body.email)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@claim_router.post("/claim/{token}/confirm-email")
def claim_confirm_email_step(token: str, body: ClaimConfirmBody, db: Database = Depends(get_db)) -> dict[str, str]:
    try:
        return confirm_email_step(db, token, email=body.email, code=body.code)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@claim_router.post("/claim/{token}/setup-totp")
def claim_setup_totp(token: str, db: Database = Depends(get_db)) -> dict[str, str]:
    return setup_totp_2fa(db, token)


@claim_router.post("/claim/{token}/confirm-totp")
def claim_confirm_totp(token: str, body: ClaimConfirmBody, db: Database = Depends(get_db)) -> dict[str, str]:
    try:
        return confirm_totp(db, token, email=body.email, code=body.code)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@claim_router.post("/claim/{token}/request-code")
def claim_request_code(
    token: str,
    body: ClaimRequestCodeBody,
    db: Database = Depends(get_db),
) -> dict[str, str]:
    """Legacy alias for POST /begin with channel=email|telegram."""
    try:
        return begin_claim(
            db,
            token,
            email=body.email,
            channel=body.channel,
            telegram_chat_id=body.telegram_chat_id,
        )
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@claim_router.post("/claim/{token}/confirm")
def claim_confirm(
    token: str,
    body: ClaimConfirmBody,
    db: Database = Depends(get_db),
) -> dict[str, str]:
    try:
        return confirm_claim(db, token, email=body.email, code=body.code)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
