# v2.3 M2 Data Provenance

This document tracks data provenance for Milestone 2 (Fusion + Forecasting, offline replay/backtesting).

## Scope

The following sources are collected by scripts in `scripts/data_acquisition/`:

- Spot pricing signals
- Cluster telemetry traces
- Cloud incident/status archives

## Source Catalog

| Dataset domain | Source | Access path | License / terms | License link | Notes |
|---|---|---|---|---|---|
| Spot pricing | AWS public spot snapshot (`spot.js`) | `fetch_spot_history.py --source spot-js` | AWS Site Terms | https://aws.amazon.com/terms/ | Snapshot feed; collect repeatedly to build local history. |
| Spot pricing (historical) | AWS EC2 Spot Price History API | `fetch_spot_history.py --source ec2-api` | AWS Service Terms | https://aws.amazon.com/service-terms/ | Requires AWS credentials and API permissions. |
| Cluster traces | Google ClusterData 2011-2 | `fetch_cluster_traces.py` | CC-BY 4.0 | https://creativecommons.org/licenses/by/4.0/ | Public trace dataset; provenance points to dataset description doc. |
| Incident archives | AWS Service Health RSS | `fetch_incident_archives.py` | AWS Site Terms | https://aws.amazon.com/terms/ | Feed content usage follows provider terms. |
| Incident archives | Google Cloud Status Atom | `fetch_incident_archives.py` | Google Terms of Service | https://policies.google.com/terms | Feed content usage follows provider terms. |
| Incident archives | Azure Status feed | `fetch_incident_archives.py` | Microsoft Terms of Use | https://www.microsoft.com/legal/terms-of-use | Feed content usage follows provider terms. |

## Reproducibility

Each acquisition script writes:

1. Data artifacts under `Data/replay/*`
2. A machine-readable provenance manifest under `Data/replay/provenance/*_manifest.json`

Manifests include:

- Generation timestamp (UTC)
- Executed command
- Source URLs
- Output file paths
- Record/file counts
- Integrity metadata (for trace archives: size + SHA-256)

## Governance Notes

- `Data/` is excluded from git; datasets remain local unless explicitly exported.
- Downstream training/evaluation outputs should reference the exact provenance manifest used.
- Before publishing derived datasets, review each provider's terms and attribution requirements.
