from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="OAR_", env_file=".env", extra="ignore")

    public_base_url: str = "http://127.0.0.1:8765"
    database_path: str = "data/registry.db"
    dev_expose_claim_codes: bool = True
    dev_expose_totp_secret: bool = True
    api_key_prefix: str = "oar_"

    # Claim: email + TOTP (both required)
    claim_require_2fa: bool = False

    # SMTP (email codes)
    smtp_host: str = ""
    smtp_port: int = 587
    smtp_user: str = ""
    smtp_password: str = ""
    smtp_from: str = ""
    smtp_use_tls: bool = True

    # Telegram (@BotFather token)
    telegram_bot_token: str = ""


settings = Settings()
