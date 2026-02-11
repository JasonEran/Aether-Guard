# SPIRE Join-Token Rotation Runbook (Docker Compose)

This runbook addresses the "join token already used/expired" error when the SPIRE agent
cannot re-attest and SVIDs stop rotating.

## Symptoms

- spire-agent logs show: `failed to attest: join token does not exist or has already been used`
- core/agent mTLS requests fail due to expired SVIDs

## Recovery Steps (Docker Compose)

1) Stop the stack (optional but recommended):

```bash
docker compose down
```

2) Remove the cached join token from the bootstrap volume:

```bash
docker run --rm -v aether-guard_spire_bootstrap:/data alpine \
  sh -lc "rm -f /data/join-token /data/trust-bundle.pem"
```

3) Re-run the bootstrap container to generate a fresh join token:

```bash
docker compose run --rm spire-bootstrap
```

4) Restart SPIRE agent + helpers so they re-attest and fetch new SVIDs:

```bash
docker compose restart spire-agent spiffe-helper-core spiffe-helper-agent
```

5) Verify in logs:

```bash
docker compose logs --tail=50 spire-agent
```

You should see `Node attestation was successful` and `Renewing X509-SVID`.

## Notes

- Join tokens are one-time use by default.
- If the token cache persists across restarts, you must clear the bootstrap volume.
- For production, prefer automated rotation and monitoring around SVID expiration.
