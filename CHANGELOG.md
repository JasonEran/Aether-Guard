# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to
Semantic Versioning.

## [Unreleased]

### Added
- OpenTelemetry collector + Jaeger stack with core/AI/dashboard exporters.
- Snapshot retention sweeper with optional S3 lifecycle configuration.
- Supply-chain workflow for SBOM generation, cosign signing, and SLSA container provenance.

### Changed
- Agent now injects W3C trace headers for HTTP requests.

### Deprecated
- 

### Removed
- 

### Fixed
- SPIRE bootstrap entry creation, Docker Desktop cgroup matching, and spiffe-helper socket config for stable mTLS.

### Security
- 

## [0.1.0] - 2026-01-19

### Added
- Initial MVP release.
