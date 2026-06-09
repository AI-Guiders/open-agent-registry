from __future__ import annotations

from pydantic import BaseModel, Field, field_validator


class RegisterAgentRequest(BaseModel):
    name: str
    description: str = ""
    skills: list[str] = Field(default_factory=list)
    seeking: list[str] = Field(default_factory=list)
    logical_line_id: str | None = None
    contributor_lines: list[str] = Field(default_factory=list)
    endpoint_url: str | None = None
    protocols: list[str] = Field(default_factory=list)

    @field_validator("skills", "seeking", "contributor_lines", "protocols", mode="before")
    @classmethod
    def coerce_list(cls, value: object) -> list[str]:
        if value is None:
            return []
        if isinstance(value, str):
            return [value]
        return list(value)


class RegisterAgentResponse(BaseModel):
    agent_id: str
    name: str
    api_key: str
    claim_url: str
    claim_status: str = "pending_claim"
    important: str = "Save api_key now; it is shown once."


class AgentPublic(BaseModel):
    id: str
    name: str
    description: str
    skills: list[str]
    seeking: list[str]
    logical_line_id: str | None
    contributor_lines: list[str]
    endpoint_url: str | None
    protocols: list[str]
    claim_status: str
    owner_email: str | None
    owner_has_totp: bool = False
    claim_method: str | None = None
    is_claimed: bool
    created_at: str
    updated_at: str


class AgentStatusResponse(BaseModel):
    status: str
    is_claimed: bool
    owner_email: str | None = None


class UpdateAgentRequest(BaseModel):
    description: str | None = None
    skills: list[str] | None = None
    seeking: list[str] | None = None
    logical_line_id: str | None = None
    contributor_lines: list[str] | None = None
    endpoint_url: str | None = None
    protocols: list[str] | None = None


class ClaimBeginBody(BaseModel):
    email: str
    channel: str = "email"  # email | telegram | totp
    telegram_chat_id: str | None = None


class ClaimRequestCodeBody(BaseModel):
    email: str
    channel: str = "email"
    telegram_chat_id: str | None = None


class ClaimConfirmBody(BaseModel):
    email: str
    code: str


class ClaimEmailOnlyBody(BaseModel):
    email: str


class SearchResponse(BaseModel):
    total: int
    agents: list[AgentPublic]
