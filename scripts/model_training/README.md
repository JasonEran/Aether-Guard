# TSMixer Baseline Training (v2.3 M2)

This folder contains the baseline training workflow for issue #35:

- Reproducible TSMixer training (seeded, deterministic flags enabled).
- ONNX export for agent-side inference.
- ONNX validation (checker + onnxruntime parity).

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
