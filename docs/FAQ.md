# FAQ

## Is this production-ready?

The repo is an MVP plus a v2.2 reference architecture. Some productization gaps remain
(mTLS, schema registry, SLSA provenance). The checklist in `README.md` tracks progress.

## How do I change the dashboard admin credentials?

Set these environment variables:

- `DASHBOARD_ADMIN_USER`
- `DASHBOARD_ADMIN_PASSWORD`

In Docker, update `docker-compose.yml` or export them before running `docker compose`.

## What data is stored in the database?

Telemetry records and command audit logs are stored in PostgreSQL/TimescaleDB. Snapshots are
stored in the local filesystem by default, or S3/MinIO when configured.

## How do I enable S3/MinIO snapshots?

Set:

- `SnapshotStorage__Provider=S3`
- `SnapshotStorage__S3__Bucket=...`
- `SnapshotStorage__S3__Endpoint=...`
- `SnapshotStorage__S3__AccessKey=...`
- `SnapshotStorage__S3__SecretKey=...`

These settings are already wired in `docker-compose.yml` for MinIO.

## Why is AI confidence always low?

The AI engine depends on `rebalanceSignal` and price volatility. If `spotPriceHistory` is empty
or stable, confidence may be low. Check the AI engine input or trigger a fire drill.

## Can I run without Docker?

Yes. Run each service locally:

- Core API: `dotnet run` in `src/services/core-dotnet/AetherGuard.Core`
- AI engine: `uvicorn main:app` in `src/services/ai-engine`
- Dashboard: `npm run dev` in `src/web/dashboard`
- Agent: build in `src/services/agent-cpp`

Use `python scripts/self_check.py --target local` to validate prerequisites.

## How do I export a diagnostics bundle?

Use the API endpoint:

```bash
curl -H "X-API-Key: $COMMAND_API_KEY" \
  "http://localhost:5000/api/v1/diagnostics/bundle?includeSnapshots=true" \
  --output aetherguard-diagnostics.zip
```

Admins can also export it from the dashboard Control Panel.

