from __future__ import annotations

import pyotp


def new_totp_secret() -> str:
    return pyotp.random_base32()


def otpauth_uri(secret: str, account_name: str, issuer: str = "OpenAgentRegistry") -> str:
    return pyotp.TOTP(secret).provisioning_uri(name=account_name, issuer_name=issuer)


def verify_totp(secret: str, code: str, *, valid_window: int = 1) -> bool:
    cleaned = code.strip().replace(" ", "")
    if not cleaned.isdigit():
        return False
    return pyotp.TOTP(secret).verify(cleaned, valid_window=valid_window)
