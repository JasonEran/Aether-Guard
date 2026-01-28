# Troubleshooting

Use this guide when the stack does not start cleanly or telemetry is missing.

## Quick checks

- Run self-check: `python scripts/self_check.py --target docker`
- Check container health: `docker compose ps`
- Stream logs: `docker compose logs -f core-service ai-service agent-service web-service`

## Common issues

### Dashboard shows "Simulation Mode"

Cause: No telemetry or audit data is arriving.

Fix:
- Ensure core-service, rabbitmq, redis, and db are healthy: `docker compose ps`
- Scale agents: `docker compose up -d --scale agent-service=2 agent-service`
- Check agent logs for registration or heartbeat failures.

### Core API is running but AI analysis is always "Unavailable"

Cause: AI engine not reachable or returning errors.

Fix:
- Ensure ai-service is running: `docker compose ps`
- Check AI logs: `docker compose logs -f ai-service`
- Verify core can reach AI: `http://ai-service:8000/analyze` should be reachable inside the network.

### Traces missing in Jaeger

Cause: OpenTelemetry exporters or collector not running.

Fix:
- Ensure otel-collector and jaeger are healthy: `docker compose ps`
- Check collector logs: `docker compose logs -f otel-collector`
- Confirm OTLP ports are reachable: `4317` (gRPC) and `4318` (HTTP).

### SPIRE certs missing or spiffe-helper logs "no identity issued"

Cause: SPIRE agent cannot resolve container IDs from cgroups (common on Docker Desktop
or cgroup v2) or the agent is not running with host PID visibility.

Fix:
- Ensure `spire-agent` uses `pid: "host"` in `docker-compose.yml`.
- Confirm `container_id_cgroup_matchers` includes `/../<id>` for Docker Desktop and
  `/docker/<id>` for Linux cgroup paths.
- Restart SPIRE services: `docker compose up -d --force-recreate spire-agent spiffe-helper-core spiffe-helper-agent`
- Verify certs from the workloads: `docker compose exec core-service ls -la /run/spiffe/certs`

### Diagnostics export fails (403 or 500)

Cause:
- 403: missing or invalid API key (or non-admin role in dashboard)
- 500: diagnostics API key not configured in core

Fix:
- Ensure `COMMAND_API_KEY` (or `DIAGNOSTICS_API_KEY`) is set in the environment.
- For dashboard export, log in as an admin user.

### Snapshot uploads or downloads fail

Cause: misconfigured snapshot storage.

Fix:
- For local storage, ensure `Data/Snapshots` is writable.
- For S3/MinIO, check `SnapshotStorage__S3__Bucket`, `Endpoint`, `AccessKey`, `SecretKey`.
- Confirm bucket exists or is auto-created.

### RabbitMQ or Redis connection errors

Cause: dependency not started or ports in use.

Fix:
- Check service health: `docker compose ps`
- If ports are already bound, stop the conflicting process or update `docker-compose.yml`.

### Postgres errors on startup

Cause: data directory is corrupted or schema changes are incompatible.

Fix:
- Export diagnostics bundle for analysis.
- If in a dev environment, remove the volume and restart:
  `docker compose down -v` and `docker compose up -d`
  (Do not do this in production.)

## Logs and diagnostics

- Core API: `docker compose logs -f core-service`
- Agent: `docker compose logs -f agent-service`
- AI engine: `docker compose logs -f ai-service`
- Dashboard: `docker compose logs -f web-service`

Use the diagnostics bundle to share logs, telemetry, and config with redaction.
