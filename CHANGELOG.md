# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to
Semantic Versioning.

## [Unreleased]

### Added
- OpenTelemetry collector + Jaeger stack with core/AI/dashboard exporters.
- Snapshot retention sweeper with optional S3 lifecycle configuration.
- Supply-chain workflow for SBOM generation, cosign signing, and SLSA container provenance.
- API key protection for telemetry ingestion and snapshot artifact endpoints.
- External signals ingestion pipeline (RSS feeds) with persisted `external_signals` table.
- External signal feed health tracking (`external_signal_feeds`) and feed status API.
- Parser regression tests for RSS/Atom feeds.
- AI Engine semantic enrichment stub (`/signals/enrich`) for v2.3 pipeline integration.
- v2.3 multimodal predictive architecture document in `docs/ARCHITECTURE-v2.3.md`.
- v2.3 delivery roadmap in `docs/ROADMAP-v2.3.md`.
- Expanded v2.3 roadmap with model choices, data sources, and validation guidance.
- Verification scripts now support API key headers and optional agent build flags.
- Optional HTTP listener when mTLS is enabled to keep dashboard/AI traffic on port 8080.

### Changed
- Agent now injects W3C trace headers for HTTP requests.
- Dashboard dependencies updated to Next.js 16.1.6.

### Deprecated
- 

### Removed
- 

### Fixed
- SPIRE bootstrap entry creation, Docker Desktop cgroup matching, and spiffe-helper socket config for stable mTLS.
- DbContext lifetime alignment to prevent startup failures with `IDbContextFactory`.
- Windows self-check now detects `npm.cmd` correctly.

### Security
- 

## [0.1.0] - 2026-01-19

### Added
- Initial MVP release.
