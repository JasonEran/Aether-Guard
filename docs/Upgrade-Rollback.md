# Upgrade and Rollback

This guide is for Docker Compose deployments. For production, use immutable image tags and
documented migrations.

## Before you upgrade

1. Export a diagnostics bundle:

```bash
curl -H "X-API-Key: $COMMAND_API_KEY" \
  "http://localhost:5000/api/v1/diagnostics/bundle?includeSnapshots=true" \
  --output aetherguard-diagnostics.zip
```

2. Back up data:

- Postgres volume: `docker run --rm -v aether-guard_postgres_data:/data -v %CD%:/backup alpine tar -czf /backup/postgres_data.tgz -C /data .`
- MinIO volume (if used): `docker run --rm -v aether-guard_minio_data:/data -v %CD%:/backup alpine tar -czf /backup/minio_data.tgz -C /data .`
- Local snapshots: copy `src/services/core-dotnet/AetherGuard.Core/Data/Snapshots`

## Upgrade steps

```bash
git pull
docker compose pull
docker compose up -d --build
```

Verify:

- `docker compose ps`
- Dashboard loads and shows telemetry
- Fire drill passes: `python scripts/fire_drill.py start`

## Rollback steps

1. Revert to the previous git revision:

```bash
git checkout <previous-tag-or-commit>
```

2. Restart services:

```bash
docker compose up -d --build
```

3. If database schema changes were applied, restore your backup volumes.

## Notes

- Avoid `docker compose down -v` in production; it deletes volumes.
- If you change configuration, re-run `python scripts/self_check.py --target docker`.
- For schema migrations, add versioned migration scripts and record applied versions.

