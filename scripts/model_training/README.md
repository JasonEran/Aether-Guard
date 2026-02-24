# TSMixer Baseline Training (v2.3 M2)

This folder contains the baseline training workflows for Milestone 2:

- Reproducible TSMixer training (seeded, deterministic flags enabled).
- ONNX export for agent-side inference.
- ONNX validation (checker + onnxruntime parity).
- Fusion baseline training with semantic vectors (`S_v`, `P_v`, `B_s`) and offline comparison.

## Prerequisites

- Python 3.10+
- Install training dependencies:

```bash
python -m pip install -r scripts/model_training/requirements.txt
```

## Quick Smoke Run

Run with synthetic fallback dataset (always available):

```bash
python scripts/model_training/train_tsmixer_baseline.py \
  --epochs 6 \
  --batch-size 128 \
  --output-dir .tmp/tsmixer-baseline-smoke
```

## Train With Acquired Spot Dataset

```bash
python scripts/model_training/train_tsmixer_baseline.py \
  --dataset-csv Data/replay/spot_history/spot_history_spot-js_YYYYMMDDTHHMMSSZ.csv \
  --epochs 20 \
  --output-dir .tmp/tsmixer-baseline-data
```

If real dataset windows are insufficient, the script switches to deterministic synthetic fallback and records the reason.

## Outputs

Each run writes:

- `tsmixer_baseline.pt`: PyTorch checkpoint (`state_dict` + normalization metadata)
- `tsmixer_baseline.onnx`: exported ONNX model
- `training_summary.json`: config, dataset source, metrics, and ONNX validation report

## Reproducibility Notes

- `--seed` controls Python, NumPy, and PyTorch RNGs.
- Deterministic PyTorch mode is enabled (`torch.use_deterministic_algorithms`).
- Data split uses deterministic shuffling with the same seed.

## Fusion Baseline (Issue #36)

Train telemetry-only and fusion models on the same dataset, then export a comparison summary:

```bash
python scripts/model_training/train_fusion_baseline.py \
  --epochs 12 \
  --output-dir .tmp/fusion-baseline-smoke
```

If the provided CSV does not contain the required semantic contract columns, the script falls back to deterministic synthetic data and records the reason.

### Fusion Input Contract (CSV)

Required semantic columns:

- `s_v_negative`
- `s_v_neutral`
- `s_v_positive`
- `p_v`
- `b_s`

Telemetry columns are configurable via `--telemetry-columns` and default to:

- `spot_price_usd`
- `cpu_utilization`
- `memory_utilization`
- `network_io`

Optional label:

- `label_preempt` (binary). If missing, labels are derived from future return + `p_v`.

Fusion outputs:

- `telemetry_only_baseline.pt`
- `fusion_baseline.pt`
- `fusion_evaluation_summary.json` (contains offline baseline metrics and deltas)
