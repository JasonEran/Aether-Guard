#!/usr/bin/env python3
"""Train fusion baseline (telemetry window + S_v/P_v/B_s) for P(preempt)."""

from __future__ import annotations

import argparse
import json
import random
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
import torch
from sklearn.metrics import average_precision_score, f1_score, precision_score, recall_score, roc_auc_score
from torch import nn
from torch.utils.data import DataLoader, TensorDataset


TELEMETRY_DEFAULT = ["spot_price_usd", "cpu_utilization", "memory_utilization", "network_io"]
SEMANTIC_DEFAULT = ["s_v_negative", "s_v_neutral", "s_v_positive", "p_v", "b_s"]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset-csv", default="", help="Optional CSV with telemetry + semantics + optional label.")
    parser.add_argument("--output-dir", default=".tmp/fusion-baseline", help="Output directory.")
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--window-size", type=int, default=24)
    parser.add_argument("--horizon", type=int, default=1)
    parser.add_argument("--epochs", type=int, default=12)
    parser.add_argument("--batch-size", type=int, default=128)
    parser.add_argument("--learning-rate", type=float, default=1e-3)
    parser.add_argument("--weight-decay", type=float, default=1e-4)
    parser.add_argument("--hidden-size", type=int, default=64)
    parser.add_argument("--dropout", type=float, default=0.1)
    parser.add_argument("--val-ratio", type=float, default=0.2)
    parser.add_argument("--test-ratio", type=float, default=0.1)
    parser.add_argument("--label-column", default="label_preempt")
    parser.add_argument("--label-threshold", type=float, default=0.03)
    parser.add_argument("--telemetry-columns", default=",".join(TELEMETRY_DEFAULT))
    parser.add_argument("--semantic-columns", default=",".join(SEMANTIC_DEFAULT))
    parser.add_argument("--synthetic-series", type=int, default=48)
    parser.add_argument("--synthetic-length", type=int, default=240)
    return parser.parse_args()


def utc_now() -> str:
    return datetime.now(tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def set_seed(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.cuda.manual_seed_all(seed)
    torch.use_deterministic_algorithms(True, warn_only=True)


def derive_labels(price: np.ndarray, p_v: np.ndarray, horizon: int, threshold: float) -> np.ndarray:
    y = np.zeros_like(price, dtype=np.float32)
    for idx in range(0, len(price) - horizon):
        f = idx + horizon
        ret = (price[f] - price[idx]) / max(abs(price[idx]), 1e-6)
        y[f] = 1.0 if (ret >= threshold or p_v[idx] >= 0.75) else 0.0
    return y


def build_windows(tel: np.ndarray, sem: np.ndarray, y: np.ndarray, window: int, horizon: int) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    xt: list[np.ndarray] = []
    xs: list[np.ndarray] = []
    yy: list[float] = []
    for end in range(window - 1, tel.shape[0] - horizon):
        start = end - window + 1
        target = end + horizon
        tw = tel[start : end + 1]
        sv = sem[end]
        if not np.isfinite(tw).all() or not np.isfinite(sv).all():
            continue
        xt.append(tw.astype(np.float32))
        xs.append(sv.astype(np.float32))
        yy.append(float(y[target]))
    if not xt:
        raise ValueError("No windows produced.")
    return np.stack(xt), np.stack(xs), np.asarray(yy, dtype=np.float32)


def load_dataset(args: argparse.Namespace) -> tuple[np.ndarray, np.ndarray, np.ndarray, dict[str, Any]]:
    telemetry_cols = [c.strip() for c in args.telemetry_columns.split(",") if c.strip()]
    semantic_cols = [c.strip() for c in args.semantic_columns.split(",") if c.strip()]
    path = Path(args.dataset_csv) if args.dataset_csv else None
    fallback_reason: str | None = None
    if path and path.exists():
        df = pd.read_csv(path)
        tel_cols = [c for c in telemetry_cols if c in df.columns]
        if tel_cols and all(c in df.columns for c in semantic_cols):
            tel = np.column_stack([pd.to_numeric(df[c], errors="coerce").to_numpy(dtype=np.float32) for c in tel_cols])
            sem = np.column_stack([pd.to_numeric(df[c], errors="coerce").to_numpy(dtype=np.float32) for c in semantic_cols])
            if args.label_column in df.columns:
                y = np.where(pd.to_numeric(df[args.label_column], errors="coerce").fillna(0.0).to_numpy() >= 0.5, 1.0, 0.0).astype(np.float32)
            else:
                y = derive_labels(tel[:, 0], sem[:, 3], args.horizon, args.label_threshold)
            x_tel, x_sem, y = build_windows(tel, sem, y, args.window_size, args.horizon)
            return x_tel, x_sem, y, {
                "source": "dataset_csv",
                "telemetry_columns": tel_cols,
                "semantic_columns": semantic_cols,
                "window_count": int(x_tel.shape[0]),
            }
        missing_sem = [c for c in semantic_cols if c not in df.columns]
        if not tel_cols:
            fallback_reason = f"missing telemetry columns: {telemetry_cols}"
        elif missing_sem:
            fallback_reason = f"missing semantic columns: {missing_sem}"
        else:
            fallback_reason = "dataset windows not generated from provided CSV"
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
        spikes = set(rng.choice(n, size=max(2, n // 45), replace=False).tolist())
        shock = np.zeros(n, dtype=np.float32)
        for i in range(1, n):
            s = float(rng.normal(0.06, 0.02)) if i in spikes else 0.0
            shock[i] = 1.0 if s else 0.0
            step = 0.0002 + rng.normal(0, 0.01) + s
            price[i] = max(0.05, price[i - 1] * (1.0 + step))
            cpu[i] = float(np.clip(0.65 * cpu[i - 1] + 0.35 * (0.45 + step * 2.2 + rng.normal(0, 0.03)), 0, 1))
            mem[i] = float(np.clip(0.75 * mem[i - 1] + 0.25 * (0.5 + abs(step) * 2.0 + rng.normal(0, 0.02)), 0, 1))
            net[i] = float(np.clip(0.60 * net[i - 1] + 0.40 * (0.38 + s * 1.8 + rng.normal(0, 0.03)), 0, 1))
        ret = np.zeros(n, dtype=np.float32)
        ret[1:] = (price[1:] - price[:-1]) / np.maximum(np.abs(price[:-1]), 1e-6)
        vol = pd.Series(ret).rolling(window=5, min_periods=1).std().fillna(0).to_numpy(dtype=np.float32)
        trend = pd.Series(price).rolling(window=12, min_periods=1).mean().to_numpy(dtype=np.float32)
        s_neg = 1.0 / (1.0 + np.exp(-( -ret * 12 + shock * 1.5)))
        s_pos = 1.0 / (1.0 + np.exp(-( ret * 10 - shock * 0.2)))
        s_neu = np.clip(1.0 - np.abs(s_pos - s_neg), 0, 1)
        norm = np.maximum(s_neg + s_neu + s_pos, 1e-6)
        s_neg, s_neu, s_pos = s_neg / norm, s_neu / norm, s_pos / norm
        p_v = 1.0 / (1.0 + np.exp(-(vol * 35 + shock * 1.8)))
        b_s = 1.0 / (1.0 + np.exp(-((trend - price) * 4.0)))
        tel = np.column_stack([price, cpu, mem, net]).astype(np.float32)
        sem = np.column_stack([s_neg, s_neu, s_pos, p_v, b_s]).astype(np.float32)
        y = derive_labels(price, p_v.astype(np.float32), args.horizon, args.label_threshold)
        xt, xs, yy = build_windows(tel, sem, y, args.window_size, args.horizon)
        tel_all.append(xt)
        sem_all.append(xs)
        y_all.append(yy)
    x_tel = np.concatenate(tel_all, axis=0)
    x_sem = np.concatenate(sem_all, axis=0)
    y = np.concatenate(y_all, axis=0)
    metadata = {
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


def split_standardize(x_tel: np.ndarray, x_sem: np.ndarray, y: np.ndarray, args: argparse.Namespace) -> tuple[list[np.ndarray], dict[str, Any]]:
    idx = np.arange(x_tel.shape[0])
    np.random.default_rng(args.seed).shuffle(idx)
    x_tel, x_sem, y = x_tel[idx], x_sem[idx], y[idx]
    total = x_tel.shape[0]
    n_test = int(total * args.test_ratio)
    n_val = int(total * args.val_ratio)
    n_train = total - n_test - n_val
    train_tel, val_tel, test_tel = x_tel[:n_train], x_tel[n_train : n_train + n_val], x_tel[n_train + n_val :]
    train_sem, val_sem, test_sem = x_sem[:n_train], x_sem[n_train : n_train + n_val], x_sem[n_train + n_val :]
    train_y, val_y, test_y = y[:n_train], y[n_train : n_train + n_val], y[n_train + n_val :]
    tel_mean = train_tel.mean(axis=(0, 1), keepdims=True)
    tel_std = np.where(train_tel.std(axis=(0, 1), keepdims=True) < 1e-6, 1.0, train_tel.std(axis=(0, 1), keepdims=True))
    sem_mean = train_sem.mean(axis=0, keepdims=True)
    sem_std = np.where(train_sem.std(axis=0, keepdims=True) < 1e-6, 1.0, train_sem.std(axis=0, keepdims=True))
    train_tel, val_tel, test_tel = (train_tel - tel_mean) / tel_std, (val_tel - tel_mean) / tel_std, (test_tel - tel_mean) / tel_std
    train_sem, val_sem, test_sem = (train_sem - sem_mean) / sem_std, (val_sem - sem_mean) / sem_std, (test_sem - sem_mean) / sem_std
    stats = {"tel_mean": tel_mean.squeeze(0).tolist(), "tel_std": tel_std.squeeze(0).tolist(), "sem_mean": sem_mean.tolist(), "sem_std": sem_std.tolist()}
    return [train_tel.astype(np.float32), train_sem.astype(np.float32), train_y.astype(np.float32), val_tel.astype(np.float32), val_sem.astype(np.float32), val_y.astype(np.float32), test_tel.astype(np.float32), test_sem.astype(np.float32), test_y.astype(np.float32)], stats


class TelemetryOnly(nn.Module):
    def __init__(self, window: int, tel_dim: int, hidden: int, dropout: float):
        super().__init__()
        self.net = nn.Sequential(nn.Flatten(), nn.Linear(window * tel_dim, hidden), nn.GELU(), nn.Dropout(dropout), nn.Linear(hidden, 1))

    def forward(self, x_tel: torch.Tensor, x_sem: torch.Tensor) -> torch.Tensor:
        del x_sem
        return self.net(x_tel).squeeze(-1)


class Fusion(nn.Module):
    def __init__(self, window: int, tel_dim: int, sem_dim: int, hidden: int, dropout: float):
        super().__init__()
        self.tel = nn.Sequential(nn.Flatten(), nn.Linear(window * tel_dim, hidden), nn.GELU())
        self.sem = nn.Sequential(nn.Linear(sem_dim, hidden), nn.GELU())
        self.cls = nn.Sequential(nn.Linear(hidden * 2, hidden), nn.GELU(), nn.Dropout(dropout), nn.Linear(hidden, 1))

    def forward(self, x_tel: torch.Tensor, x_sem: torch.Tensor) -> torch.Tensor:
        return self.cls(torch.cat([self.tel(x_tel), self.sem(x_sem)], dim=1)).squeeze(-1)


def train(model: nn.Module, train_tel: np.ndarray, train_sem: np.ndarray, train_y: np.ndarray, val_tel: np.ndarray, val_sem: np.ndarray, val_y: np.ndarray, args: argparse.Namespace) -> tuple[nn.Module, list[dict[str, float]]]:
    ds = TensorDataset(torch.from_numpy(train_tel), torch.from_numpy(train_sem), torch.from_numpy(train_y))
    dl = DataLoader(ds, batch_size=args.batch_size, shuffle=True)
    pos = float(np.sum(train_y)); neg = float(len(train_y) - pos)
    crit = nn.BCEWithLogitsLoss(pos_weight=torch.tensor([neg / pos], dtype=torch.float32)) if pos > 0 and neg > 0 else nn.BCEWithLogitsLoss()
    opt = torch.optim.AdamW(model.parameters(), lr=args.learning_rate, weight_decay=args.weight_decay)
    hist: list[dict[str, float]] = []
    best = None; best_loss = float("inf")
    for ep in range(1, args.epochs + 1):
        model.train(); total = 0.0; count = 0
        for bt, bs, by in dl:
            opt.zero_grad(set_to_none=True); lg = model(bt, bs); loss = crit(lg, by); loss.backward(); opt.step()
            total += float(loss.item()) * bt.shape[0]; count += bt.shape[0]
        val = evaluate(model, val_tel, val_sem, val_y)
        hist.append({"epoch": float(ep), "train_loss": float(total / max(count, 1)), "val_loss": float(val["loss"]), "val_f1": float(val["f1"])})
        if val["loss"] < best_loss: best_loss = val["loss"]; best = {k: v.detach().cpu().clone() for k, v in model.state_dict().items()}
    if best is None: raise RuntimeError("No best checkpoint.")
    model.load_state_dict(best)
    return model, hist


def evaluate(model: nn.Module, x_tel: np.ndarray, x_sem: np.ndarray, y: np.ndarray) -> dict[str, Any]:
    model.eval()
    with torch.no_grad():
        lg = model(torch.from_numpy(x_tel), torch.from_numpy(x_sem))
        loss = float(nn.functional.binary_cross_entropy_with_logits(lg, torch.from_numpy(y)).item())
        prob = torch.sigmoid(lg).cpu().numpy().reshape(-1)
    pred = (prob >= 0.5).astype(np.float32)
    out: dict[str, Any] = {
        "loss": loss,
        "accuracy": float(np.mean(pred == y)),
        "precision": float(precision_score(y, pred, zero_division=0)),
        "recall": float(recall_score(y, pred, zero_division=0)),
        "f1": float(f1_score(y, pred, zero_division=0)),
        "positive_rate": float(np.mean(pred)),
        "auroc": None,
        "average_precision": None,
    }
    if len(np.unique(y)) > 1:
        out["auroc"] = float(roc_auc_score(y, prob))
        out["average_precision"] = float(average_precision_score(y, prob))
    return out


def main() -> int:
    args = parse_args(); set_seed(args.seed)
    out_dir = Path(args.output_dir); out_dir.mkdir(parents=True, exist_ok=True)
    x_tel, x_sem, y, ds_meta = load_dataset(args)
    splits, norm = split_standardize(x_tel, x_sem, y, args)
    tr_t, tr_s, tr_y, va_t, va_s, va_y, te_t, te_s, te_y = splits
    tel_dim, sem_dim = tr_t.shape[2], tr_s.shape[1]

    set_seed(args.seed)
    tel_model = TelemetryOnly(args.window_size, tel_dim, args.hidden_size, args.dropout)
    tel_model, tel_hist = train(tel_model, tr_t, tr_s, tr_y, va_t, va_s, va_y, args)

    set_seed(args.seed + 1)
    fus_model = Fusion(args.window_size, tel_dim, sem_dim, args.hidden_size, args.dropout)
    fus_model, fus_hist = train(fus_model, tr_t, tr_s, tr_y, va_t, va_s, va_y, args)

    tel_m = {"train": evaluate(tel_model, tr_t, tr_s, tr_y), "val": evaluate(tel_model, va_t, va_s, va_y), "test": evaluate(tel_model, te_t, te_s, te_y)}
    fus_m = {"train": evaluate(fus_model, tr_t, tr_s, tr_y), "val": evaluate(fus_model, va_t, va_s, va_y), "test": evaluate(fus_model, te_t, te_s, te_y)}

    tel_path = out_dir / "telemetry_only_baseline.pt"; fus_path = out_dir / "fusion_baseline.pt"
    torch.save({"state_dict": tel_model.state_dict(), "normalization": norm, "created_at_utc": utc_now()}, tel_path)
    torch.save({"state_dict": fus_model.state_dict(), "normalization": norm, "created_at_utc": utc_now()}, fus_path)

    summary = {
        "run_at_utc": utc_now(),
        "command": " ".join(["python"] + sys.argv),
        "dataset": ds_meta,
        "config": {k: getattr(args, k) for k in ["seed", "window_size", "horizon", "epochs", "batch_size", "learning_rate", "weight_decay", "hidden_size", "dropout", "label_column", "label_threshold"]},
        "label_balance": {"train_positive_rate": float(np.mean(tr_y)), "val_positive_rate": float(np.mean(va_y)), "test_positive_rate": float(np.mean(te_y))},
        "models": {"telemetry_only": {"metrics": tel_m, "history": tel_hist, "artifact": str(tel_path)}, "fusion": {"metrics": fus_m, "history": fus_hist, "artifact": str(fus_path)}},
        "comparison": {
            "test_f1_delta_fusion_minus_telemetry": float(fus_m["test"]["f1"] - tel_m["test"]["f1"]),
            "test_auroc_delta_fusion_minus_telemetry": None if (fus_m["test"]["auroc"] is None or tel_m["test"]["auroc"] is None) else float(fus_m["test"]["auroc"] - tel_m["test"]["auroc"]),
        },
    }
    summary_path = out_dir / "fusion_evaluation_summary.json"
    with summary_path.open("w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2, sort_keys=True)
        f.write("\n")

    print(f"Telemetry-only model saved: {tel_path}")
    print(f"Fusion model saved: {fus_path}")
    print(f"Summary saved: {summary_path}")
    print(
        f"Test F1: telemetry={tel_m['test']['f1']:.4f} "
        f"fusion={fus_m['test']['f1']:.4f} "
        f"delta={summary['comparison']['test_f1_delta_fusion_minus_telemetry']:.4f}"
    )
    if summary["comparison"]["test_auroc_delta_fusion_minus_telemetry"] is not None:
        print(f"Test AUROC delta: {summary['comparison']['test_auroc_delta_fusion_minus_telemetry']:.4f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
