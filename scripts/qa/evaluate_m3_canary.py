#!/usr/bin/env python3
"""Evaluate Milestone 3 canary metrics and emit promote/hold/rollback decision."""

from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


@dataclass(frozen=True)
class Threshold:
    warning: float | int | None
    rollback: float | int
    direction: str  # "upper_is_bad" or "lower_is_bad"
    label: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, help="Input canary metrics JSON.")
    parser.add_argument("--output", required=True, help="Output decision JSON.")
    parser.add_argument(
        "--summary-md",
        default="",
        help="Optional markdown summary output path.",
    )
    return parser.parse_args()


def now_utc_iso() -> str:
    return datetime.now(tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as handle:
        payload = json.load(handle)
    if not isinstance(payload, dict):
        raise ValueError("Input JSON must be an object.")
    return payload


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")


def resolve_metric(metrics: dict[str, Any], key: str) -> float:
    value = metrics.get(key)
    if value is None:
        return float("nan")
    try:
        return float(value)
    except (TypeError, ValueError):
        return float("nan")


def evaluate_metric(
    *,
    metric_key: str,
    value: float,
    threshold: Threshold,
) -> dict[str, Any]:
    if value != value:  # NaN
        return {
            "metric": metric_key,
            "label": threshold.label,
            "status": "missing",
            "severity": "warning",
            "value": None,
            "warning_threshold": threshold.warning,
            "rollback_threshold": threshold.rollback,
            "message": f"Metric {metric_key} is missing.",
        }

    if threshold.direction == "upper_is_bad":
        rollback_hit = value > float(threshold.rollback)
        warning_hit = threshold.warning is not None and value > float(threshold.warning)
    else:
        rollback_hit = value < float(threshold.rollback)
        warning_hit = threshold.warning is not None and value < float(threshold.warning)

    if rollback_hit:
        status = "rollback"
        severity = "critical"
    elif warning_hit:
        status = "warning"
        severity = "warning"
    else:
        status = "ok"
        severity = "info"

    return {
        "metric": metric_key,
        "label": threshold.label,
        "status": status,
        "severity": severity,
        "value": value,
        "warning_threshold": threshold.warning,
        "rollback_threshold": threshold.rollback,
        "message": f"{metric_key}={value}",
    }


def build_thresholds() -> dict[str, Threshold]:
    return {
        "critical_incident_count": Threshold(
            warning=0,
            rollback=0,
            direction="upper_is_bad",
            label="Critical incidents",
        ),
        "heartbeat_failure_rate": Threshold(
            warning=0.02,
            rollback=0.05,
            direction="upper_is_bad",
            label="Heartbeat failure rate",
        ),
        "inference_error_rate": Threshold(
            warning=0.01,
            rollback=0.02,
            direction="upper_is_bad",
            label="Inference error rate",
        ),
        "p95_inference_latency_ms": Threshold(
            warning=35.0,
            rollback=50.0,
            direction="upper_is_bad",
            label="P95 inference latency (ms)",
        ),
        "false_positive_rate_delta": Threshold(
            warning=0.06,
            rollback=0.10,
            direction="upper_is_bad",
            label="False positive delta vs v2.2",
        ),
        "preempt_decision_rate_delta": Threshold(
            warning=0.10,
            rollback=0.15,
            direction="upper_is_bad",
            label="Preempt decision rate delta vs v2.2",
        ),
    }


def decide(checks: list[dict[str, Any]]) -> tuple[str, bool, list[str]]:
    has_rollback = any(item["status"] == "rollback" for item in checks)
    has_warning_or_missing = any(item["status"] in ("warning", "missing") for item in checks)

    if has_rollback:
        return (
            "rollback",
            True,
            [
                "Disable AG_M3_ONLINE_INFERENCE_ENABLED for canary agents.",
                "Set AG_M3_FORCE_V22_FALLBACK=true for immediate rollback.",
                "Open incident and attach canary_decision.json for audit.",
            ],
        )

    if has_warning_or_missing:
        return (
            "hold",
            False,
            [
                "Keep canary scope unchanged.",
                "Collect one more observation window and re-evaluate.",
            ],
        )

    return (
        "promote",
        False,
        [
            "Increase rollout percentage in AgentInference options.",
            "Continue monitoring rollback guardrails.",
        ],
    )


def write_summary_markdown(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        handle.write("# M3 Canary Decision Summary\n\n")
        handle.write(f"- Generated at UTC: {payload['generated_at_utc']}\n")
        handle.write(f"- Decision: **{payload['decision']}**\n")
        handle.write(f"- Rollback required: {payload['rollback_required']}\n\n")
        handle.write("| Metric | Value | Status | Rollback Threshold |\n")
        handle.write("|---|---:|---|---:|\n")
        for check in payload["checks"]:
            value = "n/a" if check["value"] is None else f"{check['value']:.6f}"
            handle.write(
                f"| {check['metric']} | {value} | {check['status']} | {check['rollback_threshold']} |\n"
            )
        handle.write("\n## Actions\n\n")
        for action in payload["actions"]:
            handle.write(f"- {action}\n")


def main() -> int:
    args = parse_args()
    input_path = Path(args.input)
    output_path = Path(args.output)
    payload = load_json(input_path)
    metrics = payload.get("metrics")
    if not isinstance(metrics, dict):
        raise ValueError("Input JSON must contain object field: metrics")

    checks: list[dict[str, Any]] = []
    for key, threshold in build_thresholds().items():
        value = resolve_metric(metrics, key)
        checks.append(evaluate_metric(metric_key=key, value=value, threshold=threshold))

    decision, rollback_required, actions = decide(checks)
    decision_payload = {
        "generated_at_utc": now_utc_iso(),
        "input_path": input_path.as_posix(),
        "decision": decision,
        "rollback_required": rollback_required,
        "checks": checks,
        "actions": actions,
    }
    write_json(output_path, decision_payload)

    if args.summary_md:
        write_summary_markdown(Path(args.summary_md), decision_payload)

    print(f"Decision: {decision}")
    print(f"Rollback required: {rollback_required}")
    print(f"Decision file: {output_path}")
    if args.summary_md:
        print(f"Summary file: {args.summary_md}")

    if decision == "rollback":
        return 20
    if decision == "hold":
        return 10
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
