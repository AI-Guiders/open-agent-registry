from __future__ import annotations

import json
from typing import Any

from fastapi import HTTPException

from open_agent_registry.auth import hash_secret, new_claim_code
from open_agent_registry.channels import normalize_email, send_email_code, send_telegram_code
from open_agent_registry.config import settings
from open_agent_registry.db import Database, _utc_now
from open_agent_registry.totp import new_totp_secret, otpauth_uri, verify_totp

CHANNEL_EMAIL = "email"
CHANNEL_TELEGRAM = "telegram"
CHANNEL_TOTP = "totp"
STEP_EMAIL_VERIFIED = "email_verified"


def _get_pending_row(conn: Any, token: str) -> Any:
    row = conn.execute("SELECT * FROM agents WHERE claim_token = ?", (token,)).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="Invalid claim token")
    if row["claim_status"] == "claimed":
        raise HTTPException(status_code=409, detail="Already claimed")
    return row


def begin_claim(
    db: Database,
    token: str,
    *,
    email: str,
    channel: str,
    telegram_chat_id: str | None = None,
) -> dict[str, str]:
    owner_email = normalize_email(email)
    channel = channel.strip().lower()
    if channel not in {CHANNEL_EMAIL, CHANNEL_TELEGRAM, CHANNEL_TOTP}:
        raise HTTPException(status_code=400, detail="channel must be email, telegram, or totp")

    with db.connect() as conn:
        row = _get_pending_row(conn, token)
        agent_name = row["name"]
        now = _utc_now()

        if channel == CHANNEL_TOTP:
            secret = new_totp_secret()
            conn.execute(
                """
                UPDATE agents SET
                    owner_email = ?,
                    pending_claim_channel = ?,
                    pending_totp_secret = ?,
                    claim_code_hash = NULL,
                    claim_step = NULL,
                    owner_telegram_chat_id = ?,
                    updated_at = ?
                WHERE claim_token = ?
                """,
                (
                    owner_email,
                    CHANNEL_TOTP,
                    secret,
                    telegram_chat_id,
                    now,
                    token,
                ),
            )
            uri = otpauth_uri(secret, account_name=f"{agent_name}:{owner_email}")
            payload = {
                "channel": CHANNEL_TOTP,
                "message": "Scan otpauth_uri in your authenticator app, then confirm with a 6-digit code.",
                "otpauth_uri": uri,
                "email": owner_email,
            }
            if settings.dev_expose_totp_secret:
                payload["dev_totp_secret"] = secret
                payload["note"] = "dev_totp_secret only when OAR_DEV_EXPOSE_TOTP_SECRET=true"
            return payload

        if channel == CHANNEL_TELEGRAM:
            if not telegram_chat_id or not str(telegram_chat_id).strip():
                raise HTTPException(status_code=400, detail="telegram_chat_id required for telegram channel")
            if not settings.telegram_bot_token:
                raise HTTPException(status_code=503, detail="Telegram bot not configured (OAR_TELEGRAM_BOT_TOKEN)")

        code = new_claim_code()
        conn.execute(
            """
            UPDATE agents SET
                owner_email = ?,
                pending_claim_channel = ?,
                pending_totp_secret = NULL,
                claim_code_hash = ?,
                claim_step = NULL,
                owner_telegram_chat_id = ?,
                updated_at = ?
            WHERE claim_token = ?
            """,
            (
                owner_email,
                channel,
                hash_secret(code),
                telegram_chat_id if channel == CHANNEL_TELEGRAM else None,
                now,
                token,
            ),
        )

    payload: dict[str, str] = {
        "channel": channel,
        "message": "Verification code issued.",
        "email": owner_email,
    }

    if channel == CHANNEL_EMAIL:
        sent = send_email_code(owner_email, agent_name, code)
        if sent:
            payload["delivery"] = "smtp"
        elif settings.dev_expose_claim_codes:
            payload["dev_code"] = code
            payload["delivery"] = "dev_json"
            payload["note"] = "Configure OAR_SMTP_* for email delivery; dev_code when OAR_DEV_EXPOSE_CLAIM_CODES=true"
        else:
            raise HTTPException(
                status_code=503,
                detail="SMTP not configured and dev codes disabled",
            )
    elif channel == CHANNEL_TELEGRAM:
        send_telegram_code(str(telegram_chat_id), agent_name, code)
        payload["delivery"] = "telegram"
        if settings.dev_expose_claim_codes:
            payload["dev_code"] = code

    return payload


def begin_claim_2fa(db: Database, token: str, *, email: str) -> dict[str, str]:
    """Step 1 of 2FA claim: email code only."""
    owner_email = normalize_email(email)
    code = new_claim_code()

    with db.connect() as conn:
        row = _get_pending_row(conn, token)
        agent_name = row["name"]
        conn.execute(
            """
            UPDATE agents SET
                owner_email = ?,
                pending_claim_channel = ?,
                pending_totp_secret = NULL,
                claim_code_hash = ?,
                claim_step = NULL,
                updated_at = ?
            WHERE claim_token = ?
            """,
            (owner_email, CHANNEL_EMAIL, hash_secret(code), _utc_now(), token),
        )

    payload: dict[str, str] = {
        "mode": "2fa",
        "step": "1",
        "next": "confirm-email then setup-totp",
        "email": owner_email,
    }
    sent = send_email_code(owner_email, agent_name, code)
    if sent:
        payload["delivery"] = "smtp"
    elif settings.dev_expose_claim_codes:
        payload["dev_code"] = code
        payload["delivery"] = "dev_json"
    else:
        raise HTTPException(status_code=503, detail="SMTP not configured and dev codes disabled")
    return payload


def setup_totp_2fa(db: Database, token: str) -> dict[str, str]:
    """Step 2 of 2FA claim: enroll authenticator after email verified."""
    with db.connect() as conn:
        row = _get_pending_row(conn, token)
        if row["claim_step"] != STEP_EMAIL_VERIFIED:
            raise HTTPException(status_code=400, detail="Complete email verification first (POST .../confirm-email)")
        secret = new_totp_secret()
        conn.execute(
            """
            UPDATE agents SET pending_totp_secret = ?, pending_claim_channel = ?, updated_at = ?
            WHERE claim_token = ?
            """,
            (secret, CHANNEL_TOTP, _utc_now(), token),
        )
        owner_email = row["owner_email"] or "owner"
        uri = otpauth_uri(secret, account_name=f"{row['name']}:{owner_email}")

    payload = {
        "mode": "2fa",
        "step": "2",
        "message": "Scan otpauth_uri, then POST .../confirm-totp",
        "otpauth_uri": uri,
    }
    if settings.dev_expose_totp_secret:
        payload["dev_totp_secret"] = secret
    return payload


def confirm_claim(db: Database, token: str, *, email: str, code: str) -> dict[str, str]:
    owner_email = normalize_email(email)
    with db.connect() as conn:
        row = _get_pending_row(conn, token)
        if (row["owner_email"] or "").lower() != owner_email:
            raise HTTPException(status_code=400, detail="Email does not match pending claim")

        channel = row["pending_claim_channel"] or CHANNEL_EMAIL
        if channel == CHANNEL_TOTP:
            secret = row["pending_totp_secret"]
            if not secret or not verify_totp(secret, code):
                raise HTTPException(status_code=400, detail="Invalid authenticator code")
            conn.execute(
                """
                UPDATE agents SET
                    claim_status = 'claimed',
                    owner_totp_secret = ?,
                    pending_totp_secret = NULL,
                    claim_code_hash = NULL,
                    claim_method = ?,
                    updated_at = ?
                WHERE claim_token = ?
                """,
                (secret, CHANNEL_TOTP, _utc_now(), token),
            )
            return {"status": "claimed", "message": "Agent claimed with authenticator.", "owner_totp_enabled": "true"}

        code_hash = hash_secret(code.strip())
        if row["claim_code_hash"] != code_hash:
            raise HTTPException(status_code=400, detail="Invalid verification code")

        method = channel if channel in {CHANNEL_EMAIL, CHANNEL_TELEGRAM} else CHANNEL_EMAIL
        conn.execute(
            """
            UPDATE agents SET
                claim_status = 'claimed',
                claim_code_hash = NULL,
                claim_method = ?,
                updated_at = ?
            WHERE claim_token = ?
            """,
            (method, _utc_now(), token),
        )
    return {"status": "claimed", "message": f"Agent claimed via {method}."}


def confirm_email_step(db: Database, token: str, *, email: str, code: str) -> dict[str, str]:
    """2FA step 1 — verify email, then setup-totp."""
    if not settings.claim_require_2fa:
        return confirm_claim(db, token, email=email, code=code)

    owner_email = normalize_email(email)
    code_hash = hash_secret(code.strip())

    with db.connect() as conn:
        row = _get_pending_row(conn, token)
        if (row["owner_email"] or "").lower() != owner_email:
            raise HTTPException(status_code=400, detail="Email does not match pending claim")
        if row["claim_code_hash"] != code_hash:
            raise HTTPException(status_code=400, detail="Invalid verification code")

        conn.execute(
            """
            UPDATE agents SET claim_step = ?, claim_code_hash = NULL, updated_at = ?
            WHERE claim_token = ?
            """,
            (STEP_EMAIL_VERIFIED, _utc_now(), token),
        )
        return {
            "status": "email_verified",
            "next": f"POST /claim/{token}/setup-totp",
        }


def confirm_totp(db: Database, token: str, *, email: str, code: str) -> dict[str, str]:
    owner_email = normalize_email(email)

    with db.connect() as conn:
        row = _get_pending_row(conn, token)
        if (row["owner_email"] or "").lower() != owner_email:
            raise HTTPException(status_code=400, detail="Email does not match pending claim")
        secret = row["pending_totp_secret"]
        if not secret:
            raise HTTPException(status_code=400, detail="TOTP not started; POST .../begin with channel=totp")

        if not verify_totp(secret, code):
            raise HTTPException(status_code=400, detail="Invalid authenticator code")

        two_fa = settings.claim_require_2fa or row["claim_step"] == STEP_EMAIL_VERIFIED
        conn.execute(
            """
            UPDATE agents SET
                claim_status = 'claimed',
                claim_code_hash = NULL,
                pending_totp_secret = NULL,
                owner_totp_secret = ?,
                claim_step = NULL,
                pending_claim_channel = ?,
                claim_method = ?,
                updated_at = ?
            WHERE claim_token = ?
            """,
            (
                secret,
                CHANNEL_TOTP,
                "2fa" if two_fa else CHANNEL_TOTP,
                _utc_now(),
                token,
            ),
        )
    method = "2fa" if two_fa else CHANNEL_TOTP
    return {
        "status": "claimed",
        "message": "Agent claimed with authenticator.",
        "owner_totp_enabled": "true",
        "claim_method": method,
    }
