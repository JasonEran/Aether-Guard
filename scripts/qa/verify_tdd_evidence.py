#!/usr/bin/env python3
"""Verify CP3407 TDD evidence ledger against git history."""

from __future__ import annotations

import argparse
import json
import subprocess
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any


@dataclass
class EntryResult:
    entry_id: str
    scope: str
    level: str
    status: str
    test_commit: str
    test_date: str
    implementation_commit: str
    implementation_date: str
    checks: list[str]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ledger",
        default="docs/CP3407-TDD-Ledger-v2.3.json",
        help="Path to TDD ledger JSON.",
    )
    parser.add_argument(
        "--output",
        default="",
        help="Optional output markdown report path.",
    )
    return parser.parse_args()


def run_git(args: list[str]) -> str:
    return subprocess.check_output(["git", *args], text=True, encoding="utf-8").strip()


def commit_exists(commit: str) -> bool:
    try:
        subprocess.check_call(
            ["git", "cat-file", "-e", f"{commit}^{{commit}}"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        return True
    except subprocess.CalledProcessError:
        return False


def commit_touches_path(commit: str, path: str) -> bool:
    raw = run_git(["show", "--name-only", "--pretty=format:", commit])
    touched = {line.strip().replace("\\", "/") for line in raw.splitlines() if line.strip()}
    return path.replace("\\", "/") in touched


def commit_date(commit: str) -> datetime:
    iso = run_git(["show", "-s", "--format=%cI", commit])
    return datetime.fromisoformat(iso.replace("Z", "+00:00"))


def verify_entry(entry: dict[str, Any]) -> EntryResult:
    entry_id = entry["id"]
    scope = entry["scope"]
    level = entry["evidence_level"].upper()
    test_file = entry["test_file"]
    impl_file = entry["implementation_file"]
    test_commit = entry["test_commit"]
    impl_commit = entry["implementation_anchor_commit"]

    checks: list[str] = []
    ok = True

    if not commit_exists(test_commit):
        ok = False
        checks.append(f"missing test commit {test_commit}")
    if not commit_exists(impl_commit):
        ok = False
        checks.append(f"missing implementation commit {impl_commit}")

    test_dt = commit_date(test_commit)
    impl_dt = commit_date(impl_commit)

    if not commit_touches_path(test_commit, test_file):
        ok = False
        checks.append(f"test commit {test_commit} does not touch {test_file}")
    if not commit_touches_path(impl_commit, impl_file):
        ok = False
        checks.append(f"implementation commit {impl_commit} does not touch {impl_file}")

    if level == "A":
        if not (test_dt < impl_dt):
            ok = False
            checks.append("level A requires test commit date < implementation commit date")
    elif level == "B":
        if test_commit != impl_commit:
            ok = False
            checks.append("level B requires co-committed evidence (same commit SHA)")
    elif level == "C":
        if not (test_dt > impl_dt):
            ok = False
            checks.append("level C requires test commit date > implementation anchor commit date")
    else:
        ok = False
        checks.append(f"unknown evidence level {level}")

    if not checks:
        checks.append("ok")

    return EntryResult(
        entry_id=entry_id,
        scope=scope,
        level=level,
        status="PASS" if ok else "FAIL",
        test_commit=test_commit,
        test_date=test_dt.date().isoformat(),
        implementation_commit=impl_commit,
        implementation_date=impl_dt.date().isoformat(),
        checks=checks,
    )


def render_markdown(ledger: dict[str, Any], results: list[EntryResult]) -> str:
    lines: list[str] = []
    lines.append("# CP3407 TDD Ledger Verification Report")
    lines.append("")
    lines.append(f"- Release: `{ledger.get('release', 'unknown')}`")
    lines.append(f"- Ledger updated: `{ledger.get('updated', 'unknown')}`")
    lines.append(f"- Entries verified: `{len(results)}`")
    lines.append("")
    lines.append("| Entry | Level | Status | Test Commit (Date) | Impl Commit (Date) |")
    lines.append("| --- | --- | --- | --- | --- |")
    for result in results:
        lines.append(
            "| {scope} | {level} | {status} | `{t}` ({td}) | `{i}` ({id}) |".format(
                scope=result.scope,
                level=result.level,
                status=result.status,
                t=result.test_commit,
                td=result.test_date,
                i=result.implementation_commit,
                id=result.implementation_date,
            )
        )
    lines.append("")
    lines.append("## Entry Notes")
    lines.append("")
    for result in results:
        lines.append(f"- **{result.entry_id}** ({result.status}): " + "; ".join(result.checks))
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    args = parse_args()
    ledger_path = Path(args.ledger)
    ledger = json.loads(ledger_path.read_text(encoding="utf-8"))
    entries = ledger.get("entries", [])
    if not isinstance(entries, list) or not entries:
        raise ValueError("Ledger must include a non-empty entries list")

    results = [verify_entry(entry) for entry in entries]
    report = render_markdown(ledger, results)

    pass_count = sum(1 for r in results if r.status == "PASS")
    fail_count = len(results) - pass_count

    print(f"TDD ledger verified: PASS={pass_count} FAIL={fail_count}")

    if args.output:
        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(report + "\n", encoding="utf-8")
        print(f"Report written: {output_path}")

    if fail_count > 0:
        for result in results:
            if result.status == "FAIL":
                print(f"[FAIL] {result.entry_id}: {'; '.join(result.checks)}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
