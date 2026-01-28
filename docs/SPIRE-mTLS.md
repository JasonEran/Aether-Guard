# SPIRE mTLS (Docker Compose)

This repo ships a SPIRE-based mTLS setup for the agent ↔ core path with
automatic certificate rotation.

## Components

- `spire-server`: issues SPIFFE identities and signs SVIDs.
- `spire-bootstrap`: creates the join token, writes the trust bundle, and
  registers workload entries based on docker labels.
- `spire-agent`: exposes the Workload API socket.
- `spiffe-helper-core` + `spiffe-helper-agent`: fetch SVIDs and write them to
  `/run/spiffe/certs` for the core and agent services.

## How It Works

1. `spire-bootstrap` generates a join token and trust bundle, then registers
   two SPIFFE IDs:
   - `spiffe://aether-guard.local/core-service`
   - `spiffe://aether-guard.local/agent-service`
2. `spiffe-helper-*` containers are labeled so the SPIRE agent attests them
   and issues the right SVIDs.
3. Core loads `svid.pem`, `svid_key.pem`, and `bundle.pem` from the shared
   volume and enforces a client allowlist.
4. Agent uses the same bundle to authenticate the core over HTTPS.

## Verification

Start the stack:

```bash
docker compose up -d
```

Check bootstrap status:

```bash
docker compose logs spire-bootstrap
```

Inspect certificates (from the services that consume them):

```bash
docker compose exec core-service ls -la /run/spiffe/certs
docker compose exec agent-service ls -la /run/spiffe/certs
```

## Notes

- The SPIRE agent mounts the Docker Engine socket to attest workloads by label.
- Docker Desktop/cgroup v2 uses nonstandard cgroup paths; the compose file and
  agent config include `pid: host` plus a `/../<id>` cgroup matcher so docker
  label attestation works out of the box.
- SPIFFE SVIDs use URI SANs rather than DNS SANs; the agent disables hostname
  verification by default (`AG_MTLS_VERIFY_HOST=false`) while still verifying
  the trust bundle.
- For strict server identity checks, use a SPIFFE-aware proxy (Envoy, Linkerd)
  or extend the client to validate SPIFFE IDs in TLS.
