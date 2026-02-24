# v2.3 M2 Fusion Model Baseline (`P(preempt)`)

This document defines the input contract and offline baseline evaluation for issue #36.

## Goal

Fuse telemetry windows with semantic exogenous vectors (`S_v`, `P_v`, `B_s`) and produce `P(preempt)`.

## Training Entry Point

- `scripts/model_training/train_fusion_baseline.py`

## Input Contract (Offline CSV)

### Required semantic columns

- `s_v_negative`
- `s_v_neutral`
- `s_v_positive`
- `p_v`
- `b_s`

### Telemetry columns

Configured by `--telemetry-columns`. Default order:

- `spot_price_usd`
- `cpu_utilization`
- `memory_utilization`
- `network_io`

At least one configured telemetry column must exist in the dataset.

### Optional label

- `label_preempt` (binary, 0/1)

If missing, labels are derived by:

- future return >= `--label-threshold` OR
- current `p_v >= 0.75`

### Windowing semantics

- `window_size`: telemetry lookback length.
- `horizon`: prediction target offset.
- Per training sample:
  - telemetry tensor: `[window_size, telemetry_dim]`
  - semantic tensor: `[semantic_dim]` at the end of window
  - label: binary target at `end + horizon`

## Offline Baseline Evaluation

The script trains and evaluates two models on the same split:

1. Telemetry-only baseline
2. Fusion baseline (telemetry branch + semantic branch)

Outputs:

- `telemetry_only_baseline.pt`
- `fusion_baseline.pt`
- `fusion_evaluation_summary.json`

Summary includes:

- train/val/test metrics: loss, accuracy, precision, recall, F1, AUROC, average precision
- comparison deltas:
  - `test_f1_delta_fusion_minus_telemetry`
  - `test_auroc_delta_fusion_minus_telemetry`

## Reproducibility

- Fixed `--seed` for Python/NumPy/PyTorch RNG.
- Deterministic PyTorch algorithms enabled.
- Deterministic split and normalization based on train partition.
