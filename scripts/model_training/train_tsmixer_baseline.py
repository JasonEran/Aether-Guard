#!/usr/bin/env python3
"""Train a reproducible TSMixer baseline and export ONNX for agent inference."""

from __future__ import annotations

import argparse
import json
import random
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
import torch
from torch import nn
from torch.utils.data import DataLoader, TensorDataset


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--dataset-csv",
        default="",
        help="Optional spot dataset CSV path (for example Data/replay/spot_history/*.csv).",
    )
    parser.add_argument(
        "--output-dir",
        default=".tmp/tsmixer-baseline",
        help="Output directory for model, ONNX, and training metadata.",
    )
    parser.add_argument("--seed", type=int, default=42, help="Global random seed.")
    parser.add_argument("--window-size", type=int, default=24, help="Input time steps.")
    parser.add_argument("--horizon", type=int, default=1, help="Label horizon in steps.")
    parser.add_argument(
        "--label-threshold",
        type=float,
        default=0.03,
        help="Future return threshold used to derive labels when label column is absent.",
    )
    parser.add_argument("--epochs", type=int, default=20, help="Training epochs.")
    parser.add_argument("--batch-size", type=int, default=128, help="Training batch size.")
    parser.add_argument("--learning-rate", type=float, default=1e-3, help="Adam learning rate.")
    parser.add_argument("--weight-decay", type=float, default=1e-4, help="AdamW weight decay.")
    parser.add_argument("--dropout", type=float, default=0.1, help="Dropout in classifier head.")
    parser.add_argument("--hidden-size", type=int, default=64, help="TSMixer hidden size.")
    parser.add_argument("--num-blocks", type=int, default=3, help="TSMixer block count.")
    parser.add_argument("--val-ratio", type=float, default=0.2, help="Validation split ratio.")
    parser.add_argument("--test-ratio", type=float, default=0.1, help="Test split ratio.")
    parser.add_argument(
        "--target-column",
        default="label_preempt",
        help="Optional binary target column in source CSV.",
    )
    parser.add_argument(
        "--price-column",
        default="spot_price_usd",
        help="Price column used for feature extraction and derived labels.",
    )
    parser.add_argument(
        "--timestamp-column",
        default="timestamp_utc",
        help="Timestamp column used for deterministic ordering when available.",
    )
    parser.add_argument(
        "--max-rows",
        type=int,
        default=0,
        help="Optional cap on loaded rows from CSV (0 means unlimited).",
    )
    parser.add_argument(
        "--synthetic-series",
        type=int,
        default=32,
        help="Synthetic series count used when real dataset has insufficient windows.",
    )
    parser.add_argument(
        "--synthetic-length",
        type=int,
        default=240,
        help="Length of each synthetic series when fallback is used.",
    )
    parser.add_argument(
        "--onnx-opset",
        type=int,
        default=17,
        help="ONNX opset version for export.",
    )
    parser.add_argument(
        "--skip-onnx-validation",
        action="store_true",
        help="Export ONNX but skip checker/runtime validation.",
    )
    return parser.parse_args()


def now_utc_iso() -> str:
    return datetime.now(tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def set_global_seed(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.cuda.manual_seed_all(seed)
    torch.use_deterministic_algorithms(True, warn_only=True)
    torch.backends.cudnn.deterministic = True
    torch.backends.cudnn.benchmark = False


def safe_float_series(series: pd.Series) -> np.ndarray:
    values = pd.to_numeric(series, errors="coerce").astype(float).to_numpy()
    return values[np.isfinite(values)]


def build_feature_matrix(prices: np.ndarray) -> np.ndarray:
    if prices.ndim != 1:
        raise ValueError("prices must be 1D")
    if prices.size < 5:
        raise ValueError("at least 5 points are required")

    returns = np.zeros_like(prices, dtype=np.float32)
    returns[1:] = (prices[1:] - prices[:-1]) / np.maximum(np.abs(prices[:-1]), 1e-6)

    rolling_mean = pd.Series(prices).rolling(window=3, min_periods=1).mean().to_numpy(dtype=np.float32)
    rolling_std = (
        pd.Series(prices)
        .rolling(window=3, min_periods=1)
        .std()
        .fillna(0.0)
        .to_numpy(dtype=np.float32)
    )

    features = np.column_stack(
        [
            prices.astype(np.float32),
            returns.astype(np.float32),
            rolling_mean.astype(np.float32),
            rolling_std.astype(np.float32),
        ]
    )
    return features


def build_derived_labels(prices: np.ndarray, horizon: int, threshold: float) -> np.ndarray:
    labels = np.zeros(prices.size, dtype=np.float32)
    if horizon <= 0:
        return labels
    for idx in range(prices.size - horizon):
        current_price = prices[idx]
        future_price = prices[idx + horizon]
        future_return = (future_price - current_price) / max(abs(current_price), 1e-6)
        labels[idx + horizon] = 1.0 if future_return >= threshold else 0.0
    return labels


def windows_from_series(
    prices: np.ndarray,
    labels: np.ndarray,
    window_size: int,
    horizon: int,
) -> tuple[list[np.ndarray], list[float]]:
    features = build_feature_matrix(prices)
    xs: list[np.ndarray] = []
    ys: list[float] = []
    max_index = prices.size - horizon
    for end_idx in range(window_size - 1, max_index):
        start_idx = end_idx - window_size + 1
        target_index = end_idx + horizon
        window = features[start_idx : end_idx + 1]
        xs.append(window.astype(np.float32))
        ys.append(float(labels[target_index]))
    return xs, ys


def prepare_windows_from_dataframe(
    df: pd.DataFrame,
    *,
    price_column: str,
    target_column: str,
    timestamp_column: str,
    window_size: int,
    horizon: int,
    threshold: float,
) -> tuple[np.ndarray, np.ndarray, dict[str, Any]]:
    if price_column not in df.columns:
        raise ValueError(f"Missing required price column: {price_column}")

    working_df = df.copy()
    if timestamp_column in working_df.columns:
        working_df[timestamp_column] = pd.to_datetime(working_df[timestamp_column], errors="coerce", utc=True)
        working_df = working_df.sort_values(timestamp_column)

    group_columns = [column for column in ("region", "instance_type") if column in working_df.columns]
    if group_columns:
        grouped = working_df.groupby(group_columns, dropna=False)
        group_frames = [frame for _, frame in grouped]
    else:
        group_frames = [working_df]

    windows: list[np.ndarray] = []
    labels: list[float] = []
    series_count = 0

    for frame in group_frames:
        prices = safe_float_series(frame[price_column])
        if prices.size < (window_size + horizon + 2):
            continue

        if target_column in frame.columns:
            target_values = pd.to_numeric(frame[target_column], errors="coerce").fillna(0.0).astype(np.float32).to_numpy()
            target_values = np.where(target_values > 0.5, 1.0, 0.0).astype(np.float32)
            if target_values.size != prices.size:
                # If coercion dropped values via safe_float_series alignment mismatch, fallback to derived labels.
                target_values = build_derived_labels(prices, horizon, threshold)
        else:
            target_values = build_derived_labels(prices, horizon, threshold)

        xs, ys = windows_from_series(prices, target_values, window_size, horizon)
        if not xs:
            continue

        windows.extend(xs)
        labels.extend(ys)
        series_count += 1

    if not windows:
        raise ValueError("No training windows could be generated from the provided dataset.")

    x = np.stack(windows).astype(np.float32)
    y = np.asarray(labels, dtype=np.float32)
    metadata = {
        "source": "dataset_csv",
        "series_count": series_count,
        "window_count": int(x.shape[0]),
    }
    return x, y, metadata


def generate_synthetic_windows(
    *,
    seed: int,
    series_count: int,
    series_length: int,
    window_size: int,
    horizon: int,
    threshold: float,
) -> tuple[np.ndarray, np.ndarray, dict[str, Any]]:
    rng = np.random.default_rng(seed)
    windows: list[np.ndarray] = []
    labels: list[float] = []

    for series_idx in range(series_count):
        trend = rng.normal(0.0002, 0.0004)
        noise = rng.normal(0.0, 0.01, size=series_length).astype(np.float32)
        shocks = np.zeros(series_length, dtype=np.float32)
        shock_positions = rng.choice(series_length, size=max(1, series_length // 60), replace=False)
        shocks[shock_positions] = rng.normal(0.08, 0.03, size=shock_positions.shape[0]).astype(np.float32)

        price = np.empty(series_length, dtype=np.float32)
        price[0] = 1.0 + float(rng.normal(0.0, 0.05))
        for idx in range(1, series_length):
            drift = trend + noise[idx] + shocks[idx]
            price[idx] = max(0.05, price[idx - 1] * (1.0 + drift))

        derived_labels = build_derived_labels(price, horizon, threshold)
        xs, ys = windows_from_series(price, derived_labels, window_size, horizon)
        windows.extend(xs)
        labels.extend(ys)

    x = np.stack(windows).astype(np.float32)
    y = np.asarray(labels, dtype=np.float32)
    metadata = {
        "source": "synthetic_fallback",
        "series_count": series_count,
        "series_length": series_length,
        "window_count": int(x.shape[0]),
    }
    return x, y, metadata


def split_dataset(
    x: np.ndarray,
    y: np.ndarray,
    *,
    val_ratio: float,
    test_ratio: float,
    seed: int,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    if x.shape[0] != y.shape[0]:
        raise ValueError("Feature and label count mismatch.")
    if not (0.0 < val_ratio < 0.5) or not (0.0 <= test_ratio < 0.5):
        raise ValueError("val_ratio/test_ratio must be in a reasonable range.")
    if val_ratio + test_ratio >= 0.8:
        raise ValueError("val_ratio + test_ratio is too large.")

    rng = np.random.default_rng(seed)
    indices = np.arange(x.shape[0])
    rng.shuffle(indices)

    x_shuffled = x[indices]
    y_shuffled = y[indices]

    total = x.shape[0]
    test_count = int(total * test_ratio)
    val_count = int(total * val_ratio)
    train_count = total - val_count - test_count

    if train_count <= 0 or val_count <= 0 or test_count <= 0:
        raise ValueError("Dataset split is too small; increase sample size.")

    x_train = x_shuffled[:train_count]
    y_train = y_shuffled[:train_count]
    x_val = x_shuffled[train_count : train_count + val_count]
    y_val = y_shuffled[train_count : train_count + val_count]
    x_test = x_shuffled[train_count + val_count :]
    y_test = y_shuffled[train_count + val_count :]
    return x_train, y_train, x_val, y_val, x_test, y_test


def standardize_features(
    x_train: np.ndarray,
    x_val: np.ndarray,
    x_test: np.ndarray,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    train_mean = x_train.mean(axis=(0, 1), keepdims=True).astype(np.float32)
    train_std = x_train.std(axis=(0, 1), keepdims=True).astype(np.float32)
    train_std = np.where(train_std < 1e-6, 1.0, train_std).astype(np.float32)

    x_train_norm = ((x_train - train_mean) / train_std).astype(np.float32)
    x_val_norm = ((x_val - train_mean) / train_std).astype(np.float32)
    x_test_norm = ((x_test - train_mean) / train_std).astype(np.float32)
    return x_train_norm, x_val_norm, x_test_norm, train_mean, train_std


class MixerBlock(nn.Module):
    def __init__(self, time_steps: int, channels: int, hidden_size: int) -> None:
        super().__init__()
        self.time_norm = nn.LayerNorm(channels)
        self.time_mlp = nn.Sequential(
            nn.Linear(time_steps, hidden_size),
            nn.GELU(),
            nn.Linear(hidden_size, time_steps),
        )
        self.feature_norm = nn.LayerNorm(channels)
        self.feature_mlp = nn.Sequential(
            nn.Linear(channels, hidden_size),
            nn.GELU(),
            nn.Linear(hidden_size, channels),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        residual = x
        mixed_time = self.time_norm(x).transpose(1, 2)
        mixed_time = self.time_mlp(mixed_time).transpose(1, 2)
        x = residual + mixed_time

        residual = x
        mixed_feature = self.feature_mlp(self.feature_norm(x))
        return residual + mixed_feature


class TSMixerBinaryClassifier(nn.Module):
    def __init__(
        self,
        *,
        time_steps: int,
        channels: int,
        hidden_size: int,
        num_blocks: int,
        dropout: float,
    ) -> None:
        super().__init__()
        self.blocks = nn.ModuleList(
            [MixerBlock(time_steps=time_steps, channels=channels, hidden_size=hidden_size) for _ in range(num_blocks)]
        )
        self.head = nn.Sequential(
            nn.LayerNorm(channels),
            nn.Flatten(),
            nn.Linear(time_steps * channels, hidden_size),
            nn.GELU(),
            nn.Dropout(dropout),
            nn.Linear(hidden_size, 1),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        for block in self.blocks:
            x = block(x)
        logits = self.head(x).squeeze(-1)
        return logits


@dataclass
class Metrics:
    loss: float
    accuracy: float
    positive_rate: float


def evaluate(
    model: nn.Module,
    x: np.ndarray,
    y: np.ndarray,
) -> Metrics:
    model.eval()
    with torch.no_grad():
        inputs = torch.from_numpy(x)
        labels = torch.from_numpy(y)
        logits = model(inputs)
        loss = nn.functional.binary_cross_entropy_with_logits(logits, labels).item()
        probabilities = torch.sigmoid(logits)
        predictions = (probabilities >= 0.5).float()
        accuracy = float((predictions == labels).float().mean().item())
        positive_rate = float(predictions.mean().item())
    return Metrics(loss=loss, accuracy=accuracy, positive_rate=positive_rate)


def train_model(
    model: nn.Module,
    *,
    x_train: np.ndarray,
    y_train: np.ndarray,
    x_val: np.ndarray,
    y_val: np.ndarray,
    epochs: int,
    batch_size: int,
    learning_rate: float,
    weight_decay: float,
) -> tuple[nn.Module, list[dict[str, float]], float]:
    train_dataset = TensorDataset(torch.from_numpy(x_train), torch.from_numpy(y_train))
    train_loader = DataLoader(train_dataset, batch_size=batch_size, shuffle=True, drop_last=False)

    optimizer = torch.optim.AdamW(model.parameters(), lr=learning_rate, weight_decay=weight_decay)
    positive_count = float(np.sum(y_train))
    negative_count = float(y_train.shape[0] - positive_count)
    if positive_count > 0 and negative_count > 0:
        pos_weight = torch.tensor([negative_count / positive_count], dtype=torch.float32)
        criterion = nn.BCEWithLogitsLoss(pos_weight=pos_weight)
    else:
        criterion = nn.BCEWithLogitsLoss()

    best_val_loss = float("inf")
    best_state: dict[str, Any] | None = None
    history: list[dict[str, float]] = []

    for epoch in range(1, epochs + 1):
        model.train()
        running_loss = 0.0
        seen = 0
        for batch_x, batch_y in train_loader:
            optimizer.zero_grad(set_to_none=True)
            logits = model(batch_x)
            loss = criterion(logits, batch_y)
            loss.backward()
            optimizer.step()
            running_loss += float(loss.item()) * batch_x.shape[0]
            seen += batch_x.shape[0]

        train_loss = running_loss / max(seen, 1)
        val_metrics = evaluate(model, x_val, y_val)
        history.append(
            {
                "epoch": float(epoch),
                "train_loss": float(train_loss),
                "val_loss": float(val_metrics.loss),
                "val_accuracy": float(val_metrics.accuracy),
            }
        )
        if val_metrics.loss < best_val_loss:
            best_val_loss = val_metrics.loss
            best_state = {name: value.detach().cpu().clone() for name, value in model.state_dict().items()}

    if best_state is None:
        raise RuntimeError("Training finished without capturing best state.")

    model.load_state_dict(best_state)
    return model, history, best_val_loss


def export_onnx(
    model: nn.Module,
    *,
    input_shape: tuple[int, int, int],
    output_path: Path,
    opset: int,
) -> None:
    model.eval()
    dummy_input = torch.randn(*input_shape, dtype=torch.float32)
    torch.onnx.export(
        model,
        dummy_input,
        output_path.as_posix(),
        export_params=True,
        opset_version=opset,
        do_constant_folding=True,
        input_names=["telemetry_window"],
        output_names=["preempt_logit"],
        dynamic_axes={"telemetry_window": {0: "batch_size"}, "preempt_logit": {0: "batch_size"}},
        dynamo=False,
    )


def validate_onnx(
    model: nn.Module,
    onnx_path: Path,
    sample_inputs: np.ndarray,
) -> dict[str, Any]:
    import onnx
    import onnxruntime as ort

    onnx_model = onnx.load(onnx_path.as_posix())
    onnx.checker.check_model(onnx_model)

    session = ort.InferenceSession(onnx_path.as_posix(), providers=["CPUExecutionProvider"])
    onnx_input_name = session.get_inputs()[0].name
    onnx_output_name = session.get_outputs()[0].name

    with torch.no_grad():
        torch_logits = model(torch.from_numpy(sample_inputs)).detach().cpu().numpy().reshape(-1)
    onnx_logits = session.run([onnx_output_name], {onnx_input_name: sample_inputs})[0].reshape(-1)

    max_abs_diff = float(np.max(np.abs(torch_logits - onnx_logits)))
    mean_abs_diff = float(np.mean(np.abs(torch_logits - onnx_logits)))
    return {
        "onnx_checker_passed": True,
        "onnxruntime_parity_passed": bool(max_abs_diff <= 1e-4),
        "max_abs_diff": max_abs_diff,
        "mean_abs_diff": mean_abs_diff,
        "sample_size": int(sample_inputs.shape[0]),
    }


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")


def main() -> int:
    args = parse_args()
    set_global_seed(args.seed)

    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    dataset_metadata: dict[str, Any]
    x: np.ndarray
    y: np.ndarray

    dataset_path = Path(args.dataset_csv) if args.dataset_csv else None
    if dataset_path and dataset_path.exists():
        dataset_df = pd.read_csv(dataset_path)
        if args.max_rows > 0:
            dataset_df = dataset_df.head(args.max_rows)
        try:
            x, y, dataset_metadata = prepare_windows_from_dataframe(
                dataset_df,
                price_column=args.price_column,
                target_column=args.target_column,
                timestamp_column=args.timestamp_column,
                window_size=args.window_size,
                horizon=args.horizon,
                threshold=args.label_threshold,
            )
        except Exception as exc:
            print(f"Dataset windows unavailable ({exc}); switching to synthetic fallback.")
            x, y, dataset_metadata = generate_synthetic_windows(
                seed=args.seed,
                series_count=args.synthetic_series,
                series_length=args.synthetic_length,
                window_size=args.window_size,
                horizon=args.horizon,
                threshold=args.label_threshold,
            )
            dataset_metadata["fallback_reason"] = str(exc)
            dataset_metadata["requested_dataset"] = str(dataset_path)
    else:
        x, y, dataset_metadata = generate_synthetic_windows(
            seed=args.seed,
            series_count=args.synthetic_series,
            series_length=args.synthetic_length,
            window_size=args.window_size,
            horizon=args.horizon,
            threshold=args.label_threshold,
        )
        if dataset_path:
            dataset_metadata["requested_dataset"] = str(dataset_path)
            dataset_metadata["fallback_reason"] = "dataset path does not exist"

    x_train, y_train, x_val, y_val, x_test, y_test = split_dataset(
        x,
        y,
        val_ratio=args.val_ratio,
        test_ratio=args.test_ratio,
        seed=args.seed,
    )
    x_train, x_val, x_test, train_mean, train_std = standardize_features(x_train, x_val, x_test)

    channels = int(x_train.shape[2])
    model = TSMixerBinaryClassifier(
        time_steps=args.window_size,
        channels=channels,
        hidden_size=args.hidden_size,
        num_blocks=args.num_blocks,
        dropout=args.dropout,
    )
    model, history, best_val_loss = train_model(
        model,
        x_train=x_train,
        y_train=y_train,
        x_val=x_val,
        y_val=y_val,
        epochs=args.epochs,
        batch_size=args.batch_size,
        learning_rate=args.learning_rate,
        weight_decay=args.weight_decay,
    )

    train_metrics = evaluate(model, x_train, y_train)
    val_metrics = evaluate(model, x_val, y_val)
    test_metrics = evaluate(model, x_test, y_test)

    model_path = output_dir / "tsmixer_baseline.pt"
    torch.save(
        {
            "state_dict": model.state_dict(),
            "window_size": args.window_size,
            "channels": channels,
            "train_mean": train_mean.squeeze(0).tolist(),
            "train_std": train_std.squeeze(0).tolist(),
            "created_at_utc": now_utc_iso(),
        },
        model_path,
    )

    onnx_path = output_dir / "tsmixer_baseline.onnx"
    export_onnx(
        model,
        input_shape=(1, args.window_size, channels),
        output_path=onnx_path,
        opset=args.onnx_opset,
    )

    onnx_validation: dict[str, Any] = {"skipped": bool(args.skip_onnx_validation)}
    if not args.skip_onnx_validation:
        sample_size = int(min(16, x_test.shape[0]))
        sample_inputs = x_test[:sample_size]
        onnx_validation = validate_onnx(model, onnx_path, sample_inputs)

    summary = {
        "run_at_utc": now_utc_iso(),
        "command": " ".join(["python"] + sys.argv),
        "config": {
            "seed": args.seed,
            "window_size": args.window_size,
            "horizon": args.horizon,
            "label_threshold": args.label_threshold,
            "epochs": args.epochs,
            "batch_size": args.batch_size,
            "learning_rate": args.learning_rate,
            "weight_decay": args.weight_decay,
            "dropout": args.dropout,
            "hidden_size": args.hidden_size,
            "num_blocks": args.num_blocks,
            "val_ratio": args.val_ratio,
            "test_ratio": args.test_ratio,
            "onnx_opset": args.onnx_opset,
        },
        "dataset": dataset_metadata,
        "shapes": {
            "x_train": list(x_train.shape),
            "x_val": list(x_val.shape),
            "x_test": list(x_test.shape),
        },
        "label_balance": {
            "train_positive_rate": float(np.mean(y_train)),
            "val_positive_rate": float(np.mean(y_val)),
            "test_positive_rate": float(np.mean(y_test)),
        },
        "metrics": {
            "best_val_loss": float(best_val_loss),
            "train": train_metrics.__dict__,
            "val": val_metrics.__dict__,
            "test": test_metrics.__dict__,
        },
        "artifacts": {
            "torch_model": str(model_path),
            "onnx_model": str(onnx_path),
        },
        "onnx_validation": onnx_validation,
        "history": history,
    }
    summary_path = output_dir / "training_summary.json"
    write_json(summary_path, summary)

    print(f"Model saved: {model_path}")
    print(f"ONNX saved: {onnx_path}")
    print(f"Summary saved: {summary_path}")
    print(
        "Metrics:"
        f" train_acc={train_metrics.accuracy:.4f}"
        f" val_acc={val_metrics.accuracy:.4f}"
        f" test_acc={test_metrics.accuracy:.4f}"
    )
    if not args.skip_onnx_validation:
        print(
            "ONNX validation:"
            f" checker={onnx_validation.get('onnx_checker_passed')}"
            f" parity={onnx_validation.get('onnxruntime_parity_passed')}"
            f" max_abs_diff={onnx_validation.get('max_abs_diff')}"
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
