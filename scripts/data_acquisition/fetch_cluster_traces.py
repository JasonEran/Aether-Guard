#!/usr/bin/env python3
"""Download public cluster trace files for offline replay/backtesting."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

USER_AGENT = "Aether-Guard-Data-Acquisition/1.0"

DEFAULT_FILES = [
    {
        "name": "machine_events_part_00000",
        "target": "machine_events/part-00000-of-00001.csv.gz",
        "url": "https://storage.googleapis.com/clusterdata-2011-2/machine_events/part-00000-of-00001.csv.gz",
    },
    {
        "name": "machine_attributes_part_00000",
        "target": "machine_attributes/part-00000-of-00001.csv.gz",
        "url": "https://storage.googleapis.com/clusterdata-2011-2/machine_attributes/part-00000-of-00001.csv.gz",
    },
    {
        "name": "task_events_part_00000",
        "target": "task_events/part-00000-of-00500.csv.gz",
        "url": "https://storage.googleapis.com/clusterdata-2011-2/task_events/part-00000-of-00500.csv.gz",
    },
    {
        "name": "task_usage_part_00000",
        "target": "task_usage/part-00000-of-00500.csv.gz",
        "url": "https://storage.googleapis.com/clusterdata-2011-2/task_usage/part-00000-of-00500.csv.gz",
    },
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output-dir",
        default="Data/replay",
        help="Root output directory for trace archives and provenance metadata.",
    )
    parser.add_argument(
        "--manifest-file",
        default="",
        help="Optional JSON manifest to replace defaults (list of {name,target,url}).",
    )
    parser.add_argument(
        "--max-files",
        type=int,
        default=4,
        help="Maximum number of files to download from the manifest.",
    )
    parser.add_argument(
        "--timeout-seconds",
        type=int,
        default=120,
        help="HTTP timeout per file.",
    )
    parser.add_argument(
        "--skip-existing",
        action="store_true",
        help="Skip files already present on disk.",
    )
    return parser.parse_args()


def utc_now_iso() -> str:
    return datetime.now(tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def run_id() -> str:
    return datetime.now(tz=timezone.utc).strftime("%Y%m%dT%H%M%SZ")


def write_json(path: Path, payload: dict[str, Any]) -> None:
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")


def load_manifest(path: str) -> list[dict[str, str]]:
    if not path:
        return list(DEFAULT_FILES)

    manifest_path = Path(path)
    payload = json.loads(manifest_path.read_text(encoding="utf-8"))

    if isinstance(payload, dict):
        entries = payload.get("files", [])
    else:
        entries = payload

    if not isinstance(entries, list):
        raise ValueError("Manifest must be a list or an object with a 'files' list.")

    normalized: list[dict[str, str]] = []
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError("Manifest entries must be objects.")
        name = str(entry.get("name", "")).strip()
        target = str(entry.get("target", "")).strip()
        url = str(entry.get("url", "")).strip()
        if not name or not target or not url:
            raise ValueError("Each manifest entry requires name, target, and url.")
        normalized.append({"name": name, "target": target, "url": url})
    return normalized


def hash_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        while True:
            chunk = handle.read(1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def download_file(url: str, target: Path, timeout_seconds: int) -> tuple[int, str, str | None]:
    request = Request(url, headers={"User-Agent": USER_AGENT})
    digest = hashlib.sha256()
    total_bytes = 0
    last_modified: str | None = None

    with urlopen(request, timeout=timeout_seconds) as response:
        last_modified = response.headers.get("Last-Modified")
        with target.open("wb") as handle:
            while True:
                chunk = response.read(1024 * 1024)
                if not chunk:
                    break
                handle.write(chunk)
                digest.update(chunk)
                total_bytes += len(chunk)

    return total_bytes, digest.hexdigest(), last_modified


def main() -> int:
    args = parse_args()

    if args.max_files <= 0:
        print("--max-files must be > 0", file=sys.stderr)
        return 2

    try:
        manifest_entries = load_manifest(args.manifest_file)
    except Exception as exc:
        print(f"Manifest load failed: {exc}", file=sys.stderr)
        return 2

    selected_entries = manifest_entries[: args.max_files]
    output_root = Path(args.output_dir)
    traces_root = output_root / "cluster_traces" / "google_clusterdata_2011_2"
    provenance_dir = output_root / "provenance"
    traces_root.mkdir(parents=True, exist_ok=True)
    provenance_dir.mkdir(parents=True, exist_ok=True)

    results: list[dict[str, Any]] = []
    success_count = 0
    failure_count = 0

    for entry in selected_entries:
        target = traces_root / entry["target"]
        target.parent.mkdir(parents=True, exist_ok=True)

        if args.skip_existing and target.exists():
            record = {
                "name": entry["name"],
                "url": entry["url"],
                "target": str(target),
                "status": "skipped",
                "bytes": target.stat().st_size,
                "sha256": hash_file(target),
                "last_modified": None,
            }
            results.append(record)
            success_count += 1
            print(f"Skipped existing file: {target}")
            continue

        try:
            total_bytes, sha256, last_modified = download_file(
                entry["url"],
                target,
                args.timeout_seconds,
            )
            record = {
                "name": entry["name"],
                "url": entry["url"],
                "target": str(target),
                "status": "downloaded",
                "bytes": total_bytes,
                "sha256": sha256,
                "last_modified": last_modified,
            }
            results.append(record)
            success_count += 1
            print(f"Downloaded {entry['name']} -> {target} ({total_bytes} bytes)")
        except (HTTPError, URLError, TimeoutError) as exc:
            record = {
                "name": entry["name"],
                "url": entry["url"],
                "target": str(target),
                "status": "failed",
                "error": str(exc),
            }
            results.append(record)
            failure_count += 1
            print(f"Failed {entry['name']}: {exc}", file=sys.stderr)

    manifest = {
        "dataset": "google_clusterdata_2011_2_sample",
        "generated_at_utc": utc_now_iso(),
        "command": " ".join(sys.argv),
        "license": "CC-BY-4.0",
        "license_url": "https://creativecommons.org/licenses/by/4.0/",
        "source_terms_url": "https://raw.githubusercontent.com/google/cluster-data/master/ClusterData2011_2.md",
        "files": results,
        "summary": {
            "requested": len(selected_entries),
            "successful": success_count,
            "failed": failure_count,
        },
    }
    manifest_path = provenance_dir / "cluster_traces_manifest.json"
    write_json(manifest_path, manifest)
    print(f"Wrote provenance manifest to {manifest_path}")

    if success_count == 0:
        return 1
    if failure_count > 0:
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
