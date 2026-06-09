from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="OAR_", env_file=".env", extra="ignore")

    public_base_url: str = "http://127.0.0.1:8765"
    database_path: str = "data/registry.db"
    dev_expose_claim_codes: bool = True
    api_key_prefix: str = "oar_"


settings = Settings()
