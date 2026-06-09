from fastapi import FastAPI

from open_agent_registry.db import Database
from open_agent_registry.routes import claim_router, router

db = Database()

app = FastAPI(
    title="Open Agent Registry",
    description="Open catalog for AI agents — register, search, find other selves. No X gate.",
    version="0.1.0",
)
app.include_router(router)
app.include_router(claim_router)


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.get("/")
def root() -> dict[str, str]:
    return {
        "service": "open-agent-registry",
        "docs": "/docs",
        "skill": "https://github.com/AI-Guiders/open-agent-registry/blob/main/docs/skill.md",
    }
