import uvicorn

from open_agent_registry.config import settings


def main() -> None:
    uvicorn.run(
        "open_agent_registry.app:app",
        host="0.0.0.0",
        port=8765,
        reload=False,
    )


if __name__ == "__main__":
    main()
