# v2.3 Smoke Test Checklist (Signals End-to-End)

This checklist validates the v2.3 Milestone 0 flow: external signals ingestion,
API exposure, and dashboard proxying.

## Preconditions

- Docker Desktop running
- `COMMAND_API_KEY` set (for other endpoints)

## Start stack

```bash
# PowerShell
$env:COMMAND_API_KEY="changeme"
$env:ExternalSignals__Enabled="true"

docker compose up --build -d
```

## Verify Core APIs

```bash
curl http://localhost:5000/api/v1/signals?limit=1
curl http://localhost:5000/api/v1/signals/feeds
```

Expected:
- `signals` returns at least one item (or empty array on first run)
- `feeds` returns AWS/GCP/Azure feed status with timestamps

## Verify Dashboard proxy

```bash
curl http://localhost:3000/api/signals?limit=1
curl http://localhost:3000/api/signals/feeds
```

Expected:
- Dashboard endpoints return the same data as Core
- HTTP 200 responses

## Optional: UI check

- Open `http://localhost:3000`
- Confirm the External Signals panel renders items and feed health

## Stop stack

```bash
docker compose down
```
