#!/usr/bin/env python3
"""Run a training/eval script twice and verify artifact hashes are identical."""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

from artifact_registry import sha256_file, write_json


def parse_args() -> tuple[argparse.Namespace, list[str]]:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--script",
        required=True,
        help="Script path to run twice (for example scripts/model_training/train_tsmixer_baseline.py).",
    )
    parser.add_argument(
        "--base-output-dir",
        required=True,
        help="Base directory where run_a and run_b outputs are generated.",
    )
    parser.add_argument(
        "--artifacts",
        required=True,
        help="Comma-separated artifact paths relative to each run output directory.",
    )
    parser.add_argument(
        "--python-executable",
        default=sys.executable,
        help="Python executable used to launch the target script.",
    )
    parser.add_argument(
        "--report-name",
        default="reproducibility_check.json",
        help="Output report file name written under base-output-dir.",
    )
    parser.add_argument(
        "--keep-runs",
        action="store_true",
        help="Keep run_a and run_b folders if verification fails.",
    )
    args, passthrough = parser.parse_known_args()
    if passthrough and passthrough[0] == "--":
        passthrough = passthrough[1:]
    return args, passthrough


def run_once(
    *,
    python_executable: str,
    script_path: Path,
    output_dir: Path,
    passthrough: list[str],
) -> subprocess.CompletedProcess[str]:
    command = [
        python_executable,
        script_path.as_posix(),
        "--output-dir",
        output_dir.as_posix(),
        *passthrough,
    ]
    return subprocess.run(command, check=False, capture_output=True, text=True)


def snapshot_artifacts(*, source_dir: Path, snapshot_dir: Path, artifacts: list[str]) -> None:
    if snapshot_dir.exists():
        shutil.rmtree(snapshot_dir)
    snapshot_dir.mkdir(parents=True, exist_ok=True)
    for artifact in artifacts:
        artifact_rel = artifact.strip()
        if not artifact_rel:
            continue
        source_path = source_dir / artifact_rel
        target_path = snapshot_dir / artifact_rel
        if not source_path.exists():
            continue
        target_path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source_path, target_path)


def compare_artifacts(
    *,
    run_a_dir: Path,
    run_b_dir: Path,
    artifacts: list[str],
) -> tuple[bool, list[dict[str, Any]]]:
    all_equal = True
    comparisons: list[dict[str, Any]] = []
    for artifact in artifacts:
        artifact_rel = artifact.strip()
        if not artifact_rel:
            continue
        path_a = run_a_dir / artifact_rel
        path_b = run_b_dir / artifact_rel
        exists_a = path_a.exists()
        exists_b = path_b.exists()
        hash_a = sha256_file(path_a) if exists_a else None
        hash_b = sha256_file(path_b) if exists_b else None
        identical = bool(exists_a and exists_b and hash_a == hash_b)
        if not identical:
            all_equal = False
        comparisons.append(
            {
                "artifact": artifact_rel,
                "run_a_exists": exists_a,
                "run_b_exists": exists_b,
                "run_a_sha256": hash_a,
                "run_b_sha256": hash_b,
                "identical": identical,
            }
        )
    return all_equal, comparisons


def main() -> int:
    args, passthrough = parse_args()
    script_path = Path(args.script)
    if not script_path.exists():
        print(f"Script not found: {script_path}", file=sys.stderr)
        return 2

    base_output_dir = Path(args.base_output_dir)
    execution_dir = base_output_dir / "exec"
    run_a_dir = base_output_dir / "run_a"
    run_b_dir = base_output_dir / "run_b"
    for run_dir in (execution_dir, run_a_dir, run_b_dir):
        if run_dir.exists():
            shutil.rmtree(run_dir)
        run_dir.mkdir(parents=True, exist_ok=True)

    artifacts = [item for item in args.artifacts.split(",") if item.strip()]

    first = run_once(
        python_executable=args.python_executable,
        script_path=script_path,
        output_dir=execution_dir,
        passthrough=passthrough,
    )
    snapshot_artifacts(source_dir=execution_dir, snapshot_dir=run_a_dir, artifacts=artifacts)

    if execution_dir.exists():
        shutil.rmtree(execution_dir)
    execution_dir.mkdir(parents=True, exist_ok=True)

    second = run_once(
        python_executable=args.python_executable,
        script_path=script_path,
        output_dir=execution_dir,
        passthrough=passthrough,
    )
    snapshot_artifacts(source_dir=execution_dir, snapshot_dir=run_b_dir, artifacts=artifacts)

    all_equal, comparisons = compare_artifacts(run_a_dir=run_a_dir, run_b_dir=run_b_dir, artifacts=artifacts)

    report = {
        "script": script_path.as_posix(),
        "python_executable": args.python_executable,
        "passthrough_args": passthrough,
        "first_run_return_code": int(first.returncode),
        "second_run_return_code": int(second.returncode),
        "all_artifacts_identical": bool(all_equal and first.returncode == 0 and second.returncode == 0),
        "artifacts": comparisons,
        "first_run_stdout": first.stdout,
        "first_run_stderr": first.stderr,
        "second_run_stdout": second.stdout,
        "second_run_stderr": second.stderr,
    }
    report_path = base_output_dir / args.report_name
    write_json(report_path, report)

    print(f"Report saved: {report_path}")
    print(f"First run rc={first.returncode}, second run rc={second.returncode}")
    print(f"Artifacts identical: {report['all_artifacts_identical']}")
    for item in comparisons:
        state = "OK" if item["identical"] else "DIFF"
        print(f"- {state}: {item['artifact']}")

    if report["all_artifacts_identical"]:
        return 0

    if not args.keep_runs:
        for run_dir in (run_a_dir, run_b_dir):
            if run_dir.exists():
                shutil.rmtree(run_dir)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
