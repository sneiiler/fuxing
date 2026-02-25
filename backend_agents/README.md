# Office Agent Backend (Python + FastAPI + LangGraph)

A backend service for the Office AI Suite. This skeleton is production-leaning:
- FastAPI app with OpenAI-compatible `/v1/chat/completions` (stub) and `/v1/health`.
- Pydantic v2 models (already included under `app/models/`).
- Config via environment variables, `.env` loader, and typed settings.
- Directory scaffolding for LangGraph, services, db, and workers.
- Basic test with `pytest` and `httpx`.

## Quickstart

```bash
python -m venv .venv && . .venv/bin/activate
pip install -U pip
pip install -r requirements.txt

# run dev server
uvicorn app.main:app --reload --port 8000
```

## Project Layout

```
app/
  api/v1/          # FastAPI routes
  core/            # config, errors, telemetry hooks
  graphs/          # LangGraph graphs and tools
  models/          # Pydantic models (contract-first)
  services/        # storage, retrieval, formatter, streaming
  db/              # postgres/redis clients
  workers/         # async jobs
  main.py          # FastAPI app entry
```

## Environment

Copy `.env.example` to `.env` and adjust values.
