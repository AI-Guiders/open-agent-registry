FROM python:3.12-slim

WORKDIR /app
COPY pyproject.toml README.md ./
COPY src ./src
RUN pip install --no-cache-dir .

ENV OAR_DATABASE_PATH=/data/registry.db
ENV OAR_PUBLIC_BASE_URL=http://127.0.0.1:8765
ENV OAR_DEV_EXPOSE_CLAIM_CODES=true

VOLUME /data
EXPOSE 8765

CMD ["uvicorn", "open_agent_registry.app:app", "--host", "0.0.0.0", "--port", "8765"]
