# v2.3 M2 Data Acquisition Scripts

This folder contains reproducible scripts for Milestone 2 dataset collection:

- Spot pricing data (for market context features).
- Cluster trace archives (for telemetry replay).
- Incident/status feed archives (for semantic signals).

All scripts write two outputs:

1. Data files under an output root (default `Data/replay`).
2. A provenance manifest under `Data/replay/provenance`.

`Data/` is gitignored in this repository, so downloaded artifacts stay local.

## Prerequisites

- Python 3.10+
- Internet access
- Optional for EC2 history mode in spot script:
  - `pip install boto3`
  - AWS credentials with `ec2:DescribeSpotPriceHistory`

## Quick Start

From repository root:

```bash
python scripts/data_acquisition/fetch_spot_history.py --source spot-js
python scripts/data_acquisition/fetch_cluster_traces.py --max-files 2
python scripts/data_acquisition/fetch_incident_archives.py --max-items-per-feed 100
```

## Script Details

### 1) Spot data

```bash
python scripts/data_acquisition/fetch_spot_history.py --source spot-js
```

- `spot-js` mode: no credentials; snapshots public AWS spot price feed.
- `ec2-api` mode: historical pull from EC2 API (needs AWS credentials).

Example EC2 API pull:

```bash
python scripts/data_acquisition/fetch_spot_history.py \
  --source ec2-api \
  --region us-east-1 \
  --instance-types c5.large,m5.large \
  --start-time 2026-01-01T00:00:00Z \
  --end-time 2026-01-31T23:59:59Z
```

### 2) Cluster traces (public sample)

```bash
python scripts/data_acquisition/fetch_cluster_traces.py --max-files 4
```

- Defaults to Google `clusterdata-2011-2` public sample files.
- Supports custom manifests with `--manifest-file`.

Manifest format:

```json
[
  {
    "name": "task_events_part_00000",
    "target": "task_events/part-00000-of-00500.csv.gz",
    "url": "https://storage.googleapis.com/clusterdata-2011-2/task_events/part-00000-of-00500.csv.gz"
  }
]
```

### 3) Incident archives

```bash
python scripts/data_acquisition/fetch_incident_archives.py --max-items-per-feed 200
```

- Defaults to AWS/GCP/Azure status feeds.
- Supports custom feed catalog with `--feeds-file`.

Feed file format:

```json
[
  {
    "name": "aws-status",
    "url": "https://status.aws.amazon.com/rss/all.rss",
    "license": "AWS Site Terms",
    "license_url": "https://aws.amazon.com/terms/"
  }
]
```

## Output Layout

Default output root (`Data/replay`):

```text
Data/replay/
  spot_history/
  cluster_traces/
    google_clusterdata_2011_2/
  incident_archives/
  provenance/
    spot_history_manifest.json
    cluster_traces_manifest.json
    incident_archives_manifest.json
```

## Provenance and Licensing

See `docs/Data-Provenance-v2.3-M2.md` for the source catalog, license/terms links, and governance notes.
