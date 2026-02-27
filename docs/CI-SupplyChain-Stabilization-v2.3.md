# v2.3 CI / Supply Chain Stabilization

Epic: #14  
Issues: #46, #47

## Goal

Stabilize heavy supply-chain workflows so they run only when relevant and remain compatible with current GitHub Actions ecosystem.

## What Changed

### Trigger policy

Both workflows now support:

- `workflow_dispatch` (manual release builds)
- `push` (only `master` and `release/**` with path filters)
- `pull_request` (to `master` with the same path filters)

This avoids running heavy image/provenance jobs for unrelated edits.

### Supply-chain hardening

Updated key actions in `supply-chain.yml`:

- `docker/build-push-action` -> `v6`
- `anchore/sbom-action` -> `v0.22.2`
- `sigstore/cosign-installer` -> `v4.0.0`

SLSA reusable workflows stay on latest upstream release (`v2.1.0`).

## Acceptance Mapping

### #46

- paths filters applied: **Done**
- manual `workflow_dispatch` retained: **Done**

### #47

- SLSA permissions path preserved for reusable generators: **Done**
- SBOM generation action upgraded/pinned to current release: **Done**
- no deprecated action references in supply-chain workflow: **Done**
