#!/usr/bin/env python3
"""Utilities for deterministic model artifact versioning and run manifests."""

from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
from pathlib import Path
from typing import Any


def canonical_json_bytes(payload: Any) -> bytes:
    return json.dumps(payload, ensure_ascii=True, sort_keys=True, separators=(",", ":")).encode("utf-8")


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        while True:
            chunk = handle.read(1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def describe_dataset_file(dataset_path: Path | None) -> dict[str, Any] | None:
    if dataset_path is None or not dataset_path.exists():
        return None
    return {
        "path": dataset_path.as_posix(),
        "sha256": sha256_file(dataset_path),
        "bytes": int(dataset_path.stat().st_size),
    }


def git_commit(repo_root: Path) -> str:
    try:
        output = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=repo_root,
            check=True,
            capture_output=True,
            text=True,
        )
        return output.stdout.strip() or "unknown"
    except Exception:
        return "unknown"


def git_is_dirty(repo_root: Path) -> bool:
    try:
        output = subprocess.run(
            ["git", "status", "--porcelain"],
            cwd=repo_root,
            check=True,
            capture_output=True,
            text=True,
        )
        return bool(output.stdout.strip())
    except Exception:
        return False


def build_run_identity(
    *,
    pipeline: str,
    run_version: str,
    config: dict[str, Any],
    dataset: dict[str, Any],
    git_sha: str,
) -> tuple[str, str]:
    payload = {
        "pipeline": pipeline,
        "run_version": run_version,
        "git_sha": git_sha,
        "config": config,
        "dataset": dataset,
    }
    fingerprint = sha256_bytes(canonical_json_bytes(payload))
    run_id = f"{run_version}-{fingerprint[:12]}"
    return run_id, fingerprint


def _versioned_filename(pipeline: str, run_id: str, role: str, source_path: Path) -> str:
    suffix = "".join(source_path.suffixes) or ".bin"
    role_slug = role.replace("_", "-")
    return f"{pipeline}-{run_id}-{role_slug}{suffix}"


def materialize_versioned_artifacts(
    *,
    output_dir: Path,
    pipeline: str,
    run_id: str,
    artifacts: dict[str, Path],
) -> dict[str, Path]:
    versioned_dir = output_dir / "versioned"
    versioned_dir.mkdir(parents=True, exist_ok=True)
    result: dict[str, Path] = {}
    for role, source_path in sorted(artifacts.items()):
        target_path = versioned_dir / _versioned_filename(pipeline, run_id, role, source_path)
        shutil.copy2(source_path, target_path)
        result[role] = target_path
    return result


def artifact_inventory(artifacts: dict[str, Path]) -> dict[str, dict[str, Any]]:
    inventory: dict[str, dict[str, Any]] = {}
    for role, path in sorted(artifacts.items()):
        inventory[role] = {
            "path": path.as_posix(),
            "sha256": sha256_file(path),
            "bytes": int(path.stat().st_size),
        }
    return inventory


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")


def write_run_manifest(
    *,
    output_dir: Path,
    pipeline: str,
    run_version: str,
    run_id: str,
    run_fingerprint: str,
    git_sha: str,
    git_dirty: bool,
    config: dict[str, Any],
    dataset: dict[str, Any],
    metrics: dict[str, Any],
    artifacts: dict[str, Path],
) -> Path:
    manifest_path = output_dir / "run_manifest.json"
    manifest = {
        "schema_version": "v1",
        "pipeline": pipeline,
        "run_version": run_version,
        "run_id": run_id,
        "run_fingerprint_sha256": run_fingerprint,
        "git": {
            "commit": git_sha,
            "dirty_worktree": git_dirty,
        },
        "config": config,
        "dataset": dataset,
        "metrics": metrics,
        "artifacts": artifact_inventory(artifacts),
    }
    write_json(manifest_path, manifest)
    return manifest_path
