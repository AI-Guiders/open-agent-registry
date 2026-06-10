from __future__ import annotations

import os
import tempfile

import pytest
from fastapi.testclient import TestClient


@pytest.fixture()
def client() -> TestClient:
    fd, path = tempfile.mkstemp(suffix=".db")
    os.close(fd)
    os.environ["OAR_DATABASE_PATH"] = path
    os.environ["OAR_PUBLIC_BASE_URL"] = "http://test.local"
    os.environ["OAR_DEV_EXPOSE_CLAIM_CODES"] = "true"

    import open_agent_registry.app as app_module
    from open_agent_registry.config import settings
    from open_agent_registry.db import Database

    settings.database_path = path
    settings.public_base_url = "http://test.local"
    settings.dev_expose_claim_codes = True
    settings.dev_expose_totp_secret = True
    settings.claim_require_2fa = False
    app_module.db = Database(path)

    with TestClient(app_module.app) as test_client:
        yield test_client

    os.unlink(path)


def test_register_search_claim(client: TestClient) -> None:
    reg = client.post(
        "/api/v1/agents/register",
        json={
            "name": "ComposerCasa",
            "description": "CASA lab line",
            "skills": ["casa", "python"],
            "logical_line_id": "composer-cursor-2026",
            "contributor_lines": ["Composer @ Cursor, 2026-06-09"],
        },
    )
    assert reg.status_code == 200, reg.text
    data = reg.json()
    api_key = data["api_key"]
    claim_url = data["claim_url"]
    token = claim_url.rsplit("/", 1)[-1]

    search_before = client.get("/api/v1/agents/search?q=Composer")
    assert search_before.status_code == 200
    assert search_before.json()["total"] == 0

    code_resp = client.post(f"/claim/{token}/request-code", json={"email": "owner@example.com"})
    assert code_resp.status_code == 200
    code = code_resp.json()["dev_code"]

    confirm = client.post(
        f"/claim/{token}/confirm",
        json={"email": "owner@example.com", "code": code},
    )
    assert confirm.status_code == 200
    assert confirm.json()["status"] == "claimed"

    me = client.get("/api/v1/agents/me", headers={"Authorization": f"Bearer {api_key}"})
    assert me.status_code == 200
    assert me.json()["is_claimed"] is True

    search_after = client.get("/api/v1/agents/search?q=CASA")
    assert search_after.status_code == 200
    assert search_after.json()["total"] == 1

    by_line = client.get("/api/v1/agents/search?logical_line_id=composer-cursor-2026&claimed_only=true")
    assert by_line.json()["total"] == 1

    public = client.get("/api/v1/agents/ComposerCasa")
    assert public.status_code == 200
    assert public.json()["name"] == "ComposerCasa"


def test_claim_via_totp(client: TestClient) -> None:
    import pyotp

    reg = client.post(
        "/api/v1/agents/register",
        json={"name": "TotpLine", "description": "TOTP claim"},
    )
    token = reg.json()["claim_url"].rsplit("/", 1)[-1]

    begin = client.post(
        f"/claim/{token}/begin",
        json={"email": "totp@example.com", "channel": "totp"},
    )
    assert begin.status_code == 200
    secret = begin.json()["dev_totp_secret"]
    code = pyotp.TOTP(secret).now()

    confirm = client.post(
        f"/claim/{token}/confirm",
        json={"email": "totp@example.com", "code": code},
    )
    assert confirm.status_code == 200
    assert confirm.json()["status"] == "claimed"

    profile = client.get("/api/v1/agents/TotpLine")
    assert profile.json()["owner_has_totp"] is True
    assert profile.json()["claim_method"] == "totp"


def test_claim_2fa_flow(client: TestClient) -> None:
    import pyotp

    import open_agent_registry.app as app_module
    from open_agent_registry.config import settings

    settings.claim_require_2fa = True

    reg = client.post(
        "/api/v1/agents/register",
        json={"name": "TwoFaLine", "description": "2FA claim"},
    )
    token = reg.json()["claim_url"].rsplit("/", 1)[-1]

    begin = client.post(f"/claim/{token}/begin-2fa", json={"email": "2fa@example.com"})
    assert begin.status_code == 200
    email_code = begin.json()["dev_code"]

    step1 = client.post(
        f"/claim/{token}/confirm-email",
        json={"email": "2fa@example.com", "code": email_code},
    )
    assert step1.json()["status"] == "email_verified"

    setup = client.post(f"/claim/{token}/setup-totp")
    secret = setup.json()["dev_totp_secret"]
    totp_code = pyotp.TOTP(secret).now()

    step2 = client.post(
        f"/claim/{token}/confirm-totp",
        json={"email": "2fa@example.com", "code": totp_code},
    )
    assert step2.json()["status"] == "claimed"
    assert step2.json()["claim_method"] == "2fa"

    settings.claim_require_2fa = False
