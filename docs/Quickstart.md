# Quickstart

This guide gets you from zero to live telemetry in under 15 minutes.

## Prerequisites

- Docker Desktop (or Docker Engine) with Docker Compose v2
- Ports available: 3000, 5000, 8000, 5432, 5672, 6379, 9000, 9001, 15672
- Python 3.10+ for helper scripts

Run the self-check before first deployment:

```bash
python scripts/self_check.py --target docker
```

## Start the stack

```bash
docker compose up --build -d
```

Open the dashboard at `http://localhost:3000` and log in with:

- Username: `admin`
- Password: `admin123`

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

