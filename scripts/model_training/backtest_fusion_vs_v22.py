#!/usr/bin/env python3
"""Backtest v2.3 fusion model against v2.2 heuristic on held-out windows."""

from __future__ import annotations

import argparse
import json
import random
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
import torch
from sklearn.metrics import average_precision_score, f1_score, precision_score, recall_score, roc_auc_score
from torch import nn


TELEMETRY_DEFAULT = ["spot_price_usd", "cpu_utilization", "memory_utilization", "network_io"]
SEMANTIC_DEFAULT = ["s_v_negative", "s_v_neutral", "s_v_positive", "p_v", "b_s"]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset-csv", default="", help="Optional replay dataset CSV.")
    parser.add_argument("--output-dir", default=".tmp/backtest-fusion-vs-v22", help="Output directory for report artifacts.")
    parser.add_argument(
        "--fusion-checkpoint",
        default=".tmp/fusion-baseline-smoke/fusion_baseline.pt",
        help="Path to fusion model checkpoint from #36 training.",
    )
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--window-size", type=int, default=24)
    parser.add_argument("--horizon", type=int, default=1)
    parser.add_argument("--backtest-ratio", type=float, default=0.3, help="Fraction of timeline used as held-out backtest.")
    parser.add_argument("--decision-threshold", type=float, default=0.5, help="Probability threshold for positive fusion decision.")
    parser.add_argument("--label-column", default="label_preempt")
    parser.add_argument("--label-threshold", type=float, default=0.03)
    parser.add_argument("--telemetry-columns", default=",".join(TELEMETRY_DEFAULT))
    parser.add_argument("--semantic-columns", default=",".join(SEMANTIC_DEFAULT))
    parser.add_argument("--synthetic-series", type=int, default=48)
    parser.add_argument("--synthetic-length", type=int, default=240)
    parser.add_argument("--autotrain-if-missing", action="store_true", default=True)
    parser.add_argument("--autotrain-epochs", type=int, default=8)
    parser.add_argument("--autotrain-output-dir", default=".tmp/fusion-baseline-autotrain")
    return parser.parse_args()


def utc_now() -> str:
    return datetime.now(tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def set_seed(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.cuda.manual_seed_all(seed)


def parse_csv_columns(value: str) -> list[str]:
    return [item.strip() for item in value.split(",") if item.strip()]


def derive_labels(price: np.ndarray, p_v: np.ndarray, horizon: int, threshold: float) -> np.ndarray:
    labels = np.zeros_like(price, dtype=np.float32)
    for idx in range(len(price) - horizon):
        f = idx + horizon
        ret = (price[f] - price[idx]) / max(abs(price[idx]), 1e-6)
        labels[f] = 1.0 if (ret >= threshold or p_v[idx] >= 0.75) else 0.0
    return labels


def build_windows(
    telemetry: np.ndarray,
    semantics: np.ndarray,
    labels: np.ndarray,
    *,
    window_size: int,
    horizon: int,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    x_tel: list[np.ndarray] = []
    x_sem: list[np.ndarray] = []
    y: list[float] = []
    for end in range(window_size - 1, telemetry.shape[0] - horizon):
        start = end - window_size + 1
        target = end + horizon
        tw = telemetry[start : end + 1]
        sv = semantics[end]
        if not np.isfinite(tw).all() or not np.isfinite(sv).all():
            continue
        x_tel.append(tw.astype(np.float32))
        x_sem.append(sv.astype(np.float32))
        y.append(float(labels[target]))
    if not x_tel:
        raise ValueError("No valid windows produced.")
    return np.stack(x_tel), np.stack(x_sem), np.asarray(y, dtype=np.float32)


def load_dataset(args: argparse.Namespace) -> tuple[np.ndarray, np.ndarray, np.ndarray, dict[str, Any]]:
    telemetry_cols = parse_csv_columns(args.telemetry_columns)
    semantic_cols = parse_csv_columns(args.semantic_columns)
    path = Path(args.dataset_csv) if args.dataset_csv else None
    fallback_reason: str | None = None

    if path and path.exists():
        df = pd.read_csv(path)
        tel_cols = [col for col in telemetry_cols if col in df.columns]
        missing_sem = [col for col in semantic_cols if col not in df.columns]
        if tel_cols and not missing_sem:
            telemetry = np.column_stack(
                [pd.to_numeric(df[col], errors="coerce").to_numpy(dtype=np.float32) for col in tel_cols]
            )
            semantics = np.column_stack(
                [pd.to_numeric(df[col], errors="coerce").to_numpy(dtype=np.float32) for col in semantic_cols]
            )
            if args.label_column in df.columns:
                labels = np.where(
                    pd.to_numeric(df[args.label_column], errors="coerce").fillna(0.0).to_numpy() >= 0.5,
                    1.0,
                    0.0,
                ).astype(np.float32)
            else:
                labels = derive_labels(telemetry[:, 0], semantics[:, 3], args.horizon, args.label_threshold)
            x_tel, x_sem, y = build_windows(
                telemetry,
                semantics,
                labels,
                window_size=args.window_size,
                horizon=args.horizon,
            )
            return x_tel, x_sem, y, {
                "source": "dataset_csv",
                "telemetry_columns": tel_cols,
                "semantic_columns": semantic_cols,
                "window_count": int(x_tel.shape[0]),
            }
        if not tel_cols:
            fallback_reason = f"missing telemetry columns: {telemetry_cols}"
        else:
            fallback_reason = f"missing semantic columns: {missing_sem}"
    elif path:
        fallback_reason = "dataset path does not exist"

    rng = np.random.default_rng(args.seed)
    tel_all: list[np.ndarray] = []
    sem_all: list[np.ndarray] = []
    y_all: list[np.ndarray] = []
    for _ in range(args.synthetic_series):
        n = args.synthetic_length
        price = np.empty(n, dtype=np.float32)
        cpu = np.empty(n, dtype=np.float32)
        mem = np.empty(n, dtype=np.float32)
        net = np.empty(n, dtype=np.float32)
        price[0] = 1.0 + rng.normal(0, 0.05)
        cpu[0], mem[0], net[0] = 0.45, 0.5, 0.4
        spike_positions = set(rng.choice(n, size=max(2, n // 45), replace=False).tolist())
        shock = np.zeros(n, dtype=np.float32)
        for i in range(1, n):
            s = float(rng.normal(0.06, 0.02)) if i in spike_positions else 0.0
            shock[i] = 1.0 if s else 0.0
            step = 0.0002 + rng.normal(0, 0.01) + s
            price[i] = max(0.05, price[i - 1] * (1.0 + step))
            cpu[i] = float(np.clip(0.65 * cpu[i - 1] + 0.35 * (0.45 + step * 2.2 + rng.normal(0, 0.03)), 0, 1))
            mem[i] = float(np.clip(0.75 * mem[i - 1] + 0.25 * (0.50 + abs(step) * 2.0 + rng.normal(0, 0.02)), 0, 1))
            net[i] = float(np.clip(0.60 * net[i - 1] + 0.40 * (0.38 + s * 1.8 + rng.normal(0, 0.03)), 0, 1))

        ret = np.zeros(n, dtype=np.float32)
        ret[1:] = (price[1:] - price[:-1]) / np.maximum(np.abs(price[:-1]), 1e-6)
        vol = pd.Series(ret).rolling(window=5, min_periods=1).std().fillna(0.0).to_numpy(dtype=np.float32)
        trend = pd.Series(price).rolling(window=12, min_periods=1).mean().to_numpy(dtype=np.float32)
        s_neg = 1.0 / (1.0 + np.exp(-(-ret * 12 + shock * 1.5)))
        s_pos = 1.0 / (1.0 + np.exp(-(ret * 10 - shock * 0.2)))
        s_neu = np.clip(1.0 - np.abs(s_pos - s_neg), 0, 1)
        norm = np.maximum(s_neg + s_neu + s_pos, 1e-6)
        s_neg, s_neu, s_pos = s_neg / norm, s_neu / norm, s_pos / norm
        p_v = 1.0 / (1.0 + np.exp(-(vol * 35 + shock * 1.8)))
        b_s = 1.0 / (1.0 + np.exp(-((trend - price) * 4.0)))

        tel = np.column_stack([price, cpu, mem, net]).astype(np.float32)
        sem = np.column_stack([s_neg, s_neu, s_pos, p_v, b_s]).astype(np.float32)
        labels = derive_labels(price, p_v.astype(np.float32), args.horizon, args.label_threshold)
        x_tel, x_sem, y = build_windows(tel, sem, labels, window_size=args.window_size, horizon=args.horizon)
        tel_all.append(x_tel)
        sem_all.append(x_sem)
        y_all.append(y)

    x_tel = np.concatenate(tel_all, axis=0)
    x_sem = np.concatenate(sem_all, axis=0)
    y = np.concatenate(y_all, axis=0)
    metadata: dict[str, Any] = {
        "source": "synthetic_fallback",
        "telemetry_columns": TELEMETRY_DEFAULT,
        "semantic_columns": SEMANTIC_DEFAULT,
        "window_count": int(x_tel.shape[0]),
    }
    if path:
        metadata["requested_dataset"] = str(path)
    if fallback_reason:
        metadata["fallback_reason"] = fallback_reason
    return x_tel, x_sem, y, metadata


class FusionModel(nn.Module):
    def __init__(self, window: int, tel_dim: int, sem_dim: int, hidden: int, dropout: float) -> None:
        super().__init__()
        self.tel = nn.Sequential(nn.Flatten(), nn.Linear(window * tel_dim, hidden), nn.GELU())
        self.sem = nn.Sequential(nn.Linear(sem_dim, hidden), nn.GELU())
        self.cls = nn.Sequential(nn.Linear(hidden * 2, hidden), nn.GELU(), nn.Dropout(dropout), nn.Linear(hidden, 1))

    def forward(self, x_tel: torch.Tensor, x_sem: torch.Tensor) -> torch.Tensor:
        return self.cls(torch.cat([self.tel(x_tel), self.sem(x_sem)], dim=1)).squeeze(-1)


@dataclass
class BacktestMetrics:
    accuracy: float
    precision: float
    recall: float
    f1: float
    auroc: float | None
    average_precision: float | None
    positive_rate: float


def calc_metrics(y_true: np.ndarray, y_pred: np.ndarray, y_prob: np.ndarray) -> BacktestMetrics:
    accuracy = float(np.mean(y_true == y_pred))
    precision = float(precision_score(y_true, y_pred, zero_division=0))
    recall = float(recall_score(y_true, y_pred, zero_division=0))
    f1 = float(f1_score(y_true, y_pred, zero_division=0))
    positive_rate = float(np.mean(y_pred))
    if len(np.unique(y_true)) < 2:
        auroc = None
        avg_precision = None
    else:
        auroc = float(roc_auc_score(y_true, y_prob))
        avg_precision = float(average_precision_score(y_true, y_prob))
    return BacktestMetrics(
        accuracy=accuracy,
        precision=precision,
        recall=recall,
        f1=f1,
        auroc=auroc,
        average_precision=avg_precision,
        positive_rate=positive_rate,
    )


def ensure_checkpoint(args: argparse.Namespace) -> Path:
    checkpoint = Path(args.fusion_checkpoint)
    if checkpoint.exists():
        return checkpoint

    if not args.autotrain_if_missing:
        raise FileNotFoundError(f"Fusion checkpoint not found: {checkpoint}")

    train_script = Path(__file__).resolve().parent / "train_fusion_baseline.py"
    command = [
        sys.executable,
        str(train_script),
        "--epochs",
        str(args.autotrain_epochs),
        "--output-dir",
        args.autotrain_output_dir,
        "--seed",
        str(args.seed),
    ]
    if args.dataset_csv:
        command.extend(["--dataset-csv", args.dataset_csv])
    result = subprocess.run(command, check=False, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"Autotrain failed.\nSTDOUT:\n{result.stdout}\nSTDERR:\n{result.stderr}")
    return Path(args.autotrain_output_dir) / "fusion_baseline.pt"


def load_v22_scorer() -> Any:
    ai_engine_dir = Path(__file__).resolve().parents[2] / "src" / "services" / "ai-engine"
    if str(ai_engine_dir) not in sys.path:
        sys.path.insert(0, str(ai_engine_dir))
    from model import RiskScorer  # type: ignore

    return RiskScorer()


def normalize_with_checkpoint(
    x_tel: np.ndarray,
    x_sem: np.ndarray,
    normalization: dict[str, Any] | None,
) -> tuple[np.ndarray, np.ndarray]:
    if not normalization:
        tel_mean = x_tel.mean(axis=(0, 1), keepdims=True)
        tel_std = np.where(x_tel.std(axis=(0, 1), keepdims=True) < 1e-6, 1.0, x_tel.std(axis=(0, 1), keepdims=True))
        sem_mean = x_sem.mean(axis=0, keepdims=True)
        sem_std = np.where(x_sem.std(axis=0, keepdims=True) < 1e-6, 1.0, x_sem.std(axis=0, keepdims=True))
    else:
        tel_mean = np.asarray(normalization.get("tel_mean"), dtype=np.float32)
        tel_std = np.asarray(normalization.get("tel_std"), dtype=np.float32)
        sem_mean = np.asarray(normalization.get("sem_mean"), dtype=np.float32)
        sem_std = np.asarray(normalization.get("sem_std"), dtype=np.float32)
        tel_mean = tel_mean.reshape(1, 1, -1)
        tel_std = np.where(tel_std.reshape(1, 1, -1) < 1e-6, 1.0, tel_std.reshape(1, 1, -1))
        sem_mean = sem_mean.reshape(1, -1)
        sem_std = np.where(sem_std.reshape(1, -1) < 1e-6, 1.0, sem_std.reshape(1, -1))
    return ((x_tel - tel_mean) / tel_std).astype(np.float32), ((x_sem - sem_mean) / sem_std).astype(np.float32)


def main() -> int:
    args = parse_args()
    set_seed(args.seed)
    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    x_tel, x_sem, y, dataset_meta = load_dataset(args)
    total = x_tel.shape[0]
    holdout_count = int(total * args.backtest_ratio)
    if holdout_count < 100:
        holdout_count = min(total, 100)
    start = max(0, total - holdout_count)

    x_tel_holdout_raw = x_tel[start:]
    x_sem_holdout_raw = x_sem[start:]
    y_holdout = y[start:]

    checkpoint_path = ensure_checkpoint(args)
    checkpoint = torch.load(checkpoint_path, map_location="cpu")
    state_dict = checkpoint.get("state_dict", checkpoint)
    normalization = checkpoint.get("normalization")

    hidden = int(state_dict["tel.1.weight"].shape[0])
    tel_dim = int(x_tel_holdout_raw.shape[2])
    sem_dim = int(x_sem_holdout_raw.shape[1])
    model = FusionModel(window=args.window_size, tel_dim=tel_dim, sem_dim=sem_dim, hidden=hidden, dropout=0.0)
    model.load_state_dict(state_dict)
    model.eval()

    x_tel_holdout, x_sem_holdout = normalize_with_checkpoint(x_tel_holdout_raw, x_sem_holdout_raw, normalization)
    with torch.no_grad():
        logits = model(torch.from_numpy(x_tel_holdout), torch.from_numpy(x_sem_holdout))
        fusion_prob = torch.sigmoid(logits).cpu().numpy().reshape(-1)
    fusion_pred = (fusion_prob >= args.decision_threshold).astype(np.float32)

    scorer = load_v22_scorer()
    heuristic_prob = np.zeros_like(y_holdout, dtype=np.float32)
    heuristic_pred = np.zeros_like(y_holdout, dtype=np.float32)
    for idx, window in enumerate(x_tel_holdout_raw):
        spot_history = [float(value) for value in window[:, 0]]
        assessment = scorer.assess_risk(
            spot_price_history=spot_history,
            rebalance_signal=False,
            capacity_score=0.5,
        )
        is_critical = str(assessment.Priority).upper() == "CRITICAL"
        heuristic_prob[idx] = 1.0 if is_critical else 0.0
        heuristic_pred[idx] = heuristic_prob[idx]

    fusion_metrics = calc_metrics(y_holdout, fusion_pred, fusion_prob)
    heuristic_metrics = calc_metrics(y_holdout, heuristic_pred, heuristic_prob)

    auroc_delta = None
    if fusion_metrics.auroc is not None and heuristic_metrics.auroc is not None:
        auroc_delta = fusion_metrics.auroc - heuristic_metrics.auroc

    summary = {
        "run_at_utc": utc_now(),
        "command": " ".join(["python"] + sys.argv),
        "dataset": dataset_meta,
        "config": {
            "seed": args.seed,
            "window_size": args.window_size,
            "horizon": args.horizon,
            "backtest_ratio": args.backtest_ratio,
            "decision_threshold": args.decision_threshold,
            "fusion_checkpoint": str(checkpoint_path),
        },
        "counts": {
            "total_windows": int(total),
            "holdout_windows": int(len(y_holdout)),
            "holdout_positive_rate": float(np.mean(y_holdout)),
        },
        "metrics": {
            "v22_heuristic": heuristic_metrics.__dict__,
            "v23_fusion": fusion_metrics.__dict__,
        },
        "comparison": {
            "f1_delta_fusion_minus_v22": float(fusion_metrics.f1 - heuristic_metrics.f1),
            "auroc_delta_fusion_minus_v22": auroc_delta,
            "recall_delta_fusion_minus_v22": float(fusion_metrics.recall - heuristic_metrics.recall),
            "precision_delta_fusion_minus_v22": float(fusion_metrics.precision - heuristic_metrics.precision),
        },
    }

    summary_path = output_dir / "backtest_summary.json"
    with summary_path.open("w", encoding="utf-8") as handle:
        json.dump(summary, handle, indent=2, sort_keys=True)
        handle.write("\n")

    report = output_dir / "backtest_report.md"
    with report.open("w", encoding="utf-8") as handle:
        handle.write("# Backtest Report: v2.3 Fusion vs v2.2 Heuristic\n\n")
        handle.write(f"- Generated at UTC: {summary['run_at_utc']}\n")
        handle.write(f"- Hold-out windows: {summary['counts']['holdout_windows']}\n")
        handle.write(f"- Hold-out positive rate: {summary['counts']['holdout_positive_rate']:.4f}\n\n")
        handle.write("| Strategy | Accuracy | Precision | Recall | F1 | AUROC | AP |\n")
        handle.write("|---|---:|---:|---:|---:|---:|---:|\n")
        handle.write(
            f"| v2.2 heuristic | {heuristic_metrics.accuracy:.4f} | {heuristic_metrics.precision:.4f} | "
            f"{heuristic_metrics.recall:.4f} | {heuristic_metrics.f1:.4f} | "
            f"{'n/a' if heuristic_metrics.auroc is None else f'{heuristic_metrics.auroc:.4f}'} | "
            f"{'n/a' if heuristic_metrics.average_precision is None else f'{heuristic_metrics.average_precision:.4f}'} |\n"
        )
        handle.write(
            f"| v2.3 fusion | {fusion_metrics.accuracy:.4f} | {fusion_metrics.precision:.4f} | "
            f"{fusion_metrics.recall:.4f} | {fusion_metrics.f1:.4f} | "
            f"{'n/a' if fusion_metrics.auroc is None else f'{fusion_metrics.auroc:.4f}'} | "
            f"{'n/a' if fusion_metrics.average_precision is None else f'{fusion_metrics.average_precision:.4f}'} |\n\n"
        )
        handle.write("## Deltas (fusion - v2.2)\n\n")
        handle.write(f"- F1 delta: {summary['comparison']['f1_delta_fusion_minus_v22']:.4f}\n")
        handle.write(f"- Recall delta: {summary['comparison']['recall_delta_fusion_minus_v22']:.4f}\n")
        handle.write(f"- Precision delta: {summary['comparison']['precision_delta_fusion_minus_v22']:.4f}\n")
        if auroc_delta is None:
            handle.write("- AUROC delta: n/a\n")
        else:
            handle.write(f"- AUROC delta: {auroc_delta:.4f}\n")

    print(f"Backtest summary saved: {summary_path}")
    print(f"Backtest report saved: {report}")
    print(
        "F1 comparison:"
        f" v2.2={heuristic_metrics.f1:.4f}"
        f" fusion={fusion_metrics.f1:.4f}"
        f" delta={summary['comparison']['f1_delta_fusion_minus_v22']:.4f}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
