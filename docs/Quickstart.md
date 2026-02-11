# Quickstart

This guide gets you from zero to live telemetry in under 15 minutes.

## Prerequisites

- Docker Desktop (or Docker Engine) with Docker Compose v2
- Ports available: 3000, 5000, 5001, 8000, 4317, 4318, 5432, 5672, 6379, 9000, 9001, 15672, 16686
- Python 3.10+ for helper scripts

Run the self-check before first deployment:

```bash
python scripts/self_check.py --target docker
```

## Start the stack

```bash
# PowerShell
$env:COMMAND_API_KEY="changeme"
# Bash
export COMMAND_API_KEY=changeme

docker compose up --build -d
```

For SPIRE-based mTLS details, see `docs/SPIRE-mTLS.md`.
For SPIRE join-token recovery, see `docs/Runbook-SPIRE-JoinToken.md`.
For observability setup, see `docs/Observability.md`.

Open the dashboard at `http://localhost:3000` and log in with:

- Username: `admin`
- Password: `admin123`

Open Jaeger at `http://localhost:16686` to view traces.

## Optional: Enable external signals (v2.3 Milestone 0)

External signals ingestion is disabled by default. To enable:

```bash
# PowerShell
$env:ExternalSignals__Enabled="true"
# Optional retention tuning
$env:ExternalSignals__RetentionDays="30"
$env:ExternalSignals__CleanupBatchSize="500"
# Bash
export ExternalSignals__Enabled=true
export ExternalSignals__RetentionDays=30
export ExternalSignals__CleanupBatchSize=500
```

Then restart the core service. Signals are accessible via:

```
GET /api/v1/signals?limit=50
```

Feed health status is available via:

```
GET /api/v1/signals/feeds
```

Smoke test checklist: `docs/QA-SmokeTest-v2.3.md`.

## Optional: Enable semantic enrichment (v2.3 Milestone 1)

The AI engine defaults to a FinBERT-based enricher when dependencies are available.
You can force the provider via environment variables:

```bash
# PowerShell
$env:AI_ENRICH_PROVIDER="finbert"   # or "heuristic"
$env:AI_FINBERT_MODEL="ProsusAI/finbert"

# Bash
export AI_ENRICH_PROVIDER=finbert
export AI_FINBERT_MODEL=ProsusAI/finbert
```

Note: the first FinBERT run downloads model weights and can take a few minutes.
Set `AI_ENRICH_PROVIDER=heuristic` if you need a fast, offline fallback.

Schema details:

```
GET /signals/enrich/schema
```

If you want to simulate migrations, start at least two agents:

```bash
docker compose up -d --scale agent-service=2 agent-service
```

## Trigger a fire drill

```bash
python scripts/fire_drill.py start
```

You should see risk state changes and migration activity in the dashboard.

Reset back to stable:

```bash
python scripts/fire_drill.py stop
```

## Snapshot storage options

Default: local filesystem under `src/services/core-dotnet/AetherGuard.Core/Data/Snapshots`.

To use S3/MinIO (recommended for production), set `SnapshotStorage__Provider=S3` and
provide bucket credentials (see `docker-compose.yml`).

## Diagnostics bundle

Export a support bundle (config redacted, recent telemetry/audits, snapshot manifest,
optional snapshots):

```bash
curl -H "X-API-Key: $COMMAND_API_KEY" \
  "http://localhost:5000/api/v1/diagnostics/bundle?includeSnapshots=true" \
  --output aetherguard-diagnostics.zip
```

Admins can also export a bundle from the dashboard Control Panel.
