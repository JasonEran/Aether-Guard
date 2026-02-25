# v2.3 M2 Model Artifact Versioning + Reproducible Runs

This document captures the delivery for issue #38 (`[Ops] Model artifact versioning + reproducible runs`).

## Goal

Make offline model artifacts release-safe and reproducible for Milestone 2.

## Implemented Components

- Shared utility: `scripts/model_training/artifact_registry.py`
- Repro check runner: `scripts/model_training/verify_reproducible_run.py`
- Integrated into:
  - `scripts/model_training/train_tsmixer_baseline.py`
  - `scripts/model_training/train_fusion_baseline.py`
  - `scripts/model_training/backtest_fusion_vs_v22.py`

## Artifact Naming / Versioning Scheme

Each run computes:

- `run_version` (CLI flag, default `v2.3-m2`)
- deterministic `run_id` = `<run_version>-<12-char-fingerprint>`
- full `run_fingerprint_sha256` derived from config + dataset descriptor + git commit

Each run outputs:

- base artifacts (legacy names kept for compatibility)
- `run_manifest.json` with file hashes and provenance metadata
- `versioned/` copies named:
  - `<pipeline>-<run_id>-<artifact-role>.<ext>`

## Run Manifest Schema (`run_manifest.json`)

`schema_version: v1` payload includes:

- pipeline metadata (`pipeline`, `run_version`, `run_id`, `run_fingerprint_sha256`)
- git metadata (`commit`, `dirty_worktree`)
- deterministic run config
- dataset/input descriptors (including file hash when a file path exists)
- key metrics used for promotion decisions
- artifact inventory (`path`, `sha256`, `bytes`)

## Reproducibility Verification

Use `verify_reproducible_run.py` to execute the same command twice and compare artifact hashes.

TSMixer example:

```bash
python scripts/model_training/verify_reproducible_run.py \
  --script scripts/model_training/train_tsmixer_baseline.py \
  --base-output-dir .tmp/repro-check/tsmixer \
  --artifacts tsmixer_baseline.pt,tsmixer_baseline.onnx,training_summary.json,run_manifest.json \
  -- --epochs 6 --batch-size 128
```

Fusion example:

```bash
python scripts/model_training/verify_reproducible_run.py \
  --script scripts/model_training/train_fusion_baseline.py \
  --base-output-dir .tmp/repro-check/fusion \
  --artifacts telemetry_only_baseline.pt,fusion_baseline.pt,fusion_evaluation_summary.json,run_manifest.json \
  -- --epochs 8 --batch-size 128
```

Verification report location:

- `<base-output-dir>/reproducibility_check.json`

Acceptance is met when `all_artifacts_identical` is `true`.

## Acceptance Criteria Mapping

- [x] Artifact naming/versioning scheme
- [x] Re-run produces identical outputs (validated by hash comparison)
