# v2.3 M2 Backtesting Harness (v2.3 Fusion vs v2.2 Heuristic)

This document describes the offline backtesting runner delivered for issue #37.

## Goal

Validate v2.3 fusion model improvements against v2.2 heuristic decisions on held-out windows.

## Runner

- `scripts/model_training/backtest_fusion_vs_v22.py`

## Inputs

- Fusion checkpoint (`fusion_baseline.pt`) from issue #36.
- Optional dataset CSV with telemetry + semantic columns.
- If dataset contract is not met, deterministic synthetic fallback is used and `fallback_reason` is recorded.

## Compared Strategies

1. **v2.2 heuristic**
   - Uses legacy `RiskScorer` decision (`CRITICAL` => positive preemption signal).
2. **v2.3 fusion**
   - Uses fusion model probability with configurable decision threshold.

## Held-Out Backtest Protocol

- Build chronological windows from replay dataset.
- Reserve the tail portion (`backtest_ratio`) as held-out period.
- Evaluate both strategies on the same held-out windows.

## Metrics

Reported per strategy:

- Accuracy
- Precision
- Recall
- F1
- AUROC (if both classes present)
- Average Precision (if both classes present)
- Positive prediction rate

Reported deltas:

- `f1_delta_fusion_minus_v22`
- `recall_delta_fusion_minus_v22`
- `precision_delta_fusion_minus_v22`
- `auroc_delta_fusion_minus_v22`

## Outputs

Per run output directory contains:

- `backtest_summary.json`
- `backtest_report.md`
