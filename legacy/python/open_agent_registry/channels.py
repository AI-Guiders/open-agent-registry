from __future__ import annotations

import logging
import smtplib
from email.message import EmailMessage

import httpx

from open_agent_registry.config import settings

logger = logging.getLogger(__name__)

ClaimChannel = str  # "email" | "telegram" | "totp"


def normalize_email(email: str) -> str:
    value = email.strip().lower()
    if "@" not in value or len(value) > 320:
        raise ValueError("Invalid email")
    return value


def send_email_code(to_email: str, agent_name: str, code: str) -> bool:
    """Send claim code via SMTP. Returns True if sent, False if SMTP not configured."""
    if not settings.smtp_host:
        return False

    message = EmailMessage()
    message["Subject"] = f"Open Agent Registry — claim code for {agent_name}"
    message["From"] = settings.smtp_from or settings.smtp_user or "noreply@open-agent-registry.local"
    message["To"] = to_email
    message.set_content(
        f"Your verification code for agent «{agent_name}»:\n\n{code}\n\n"
        f"If you did not request this, ignore this message."
    )

    with smtplib.SMTP(settings.smtp_host, settings.smtp_port, timeout=30) as smtp:
        if settings.smtp_use_tls:
            smtp.starttls()
        if settings.smtp_user and settings.smtp_password:
            smtp.login(settings.smtp_user, settings.smtp_password)
        smtp.send_message(message)
    return True


def send_telegram_code(chat_id: str, agent_name: str, code: str) -> bool:
    if not settings.telegram_bot_token:
        return False

    text = (
        f"Open Agent Registry\n"
        f"Claim code for «{agent_name}»: `{code}`\n"
        f"(enter on claim page)"
    )
    url = f"https://api.telegram.org/bot{settings.telegram_bot_token}/sendMessage"
    response = httpx.post(
        url,
        json={"chat_id": chat_id, "text": text, "parse_mode": "Markdown"},
        timeout=30.0,
    )
    response.raise_for_status()
    payload = response.json()
    if not payload.get("ok"):
        raise RuntimeError(payload.get("description", "Telegram API error"))
    return True
