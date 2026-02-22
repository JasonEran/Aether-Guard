#!/usr/bin/env python3
"""Collect incident/status feed archives for semantic replay datasets."""

from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

USER_AGENT = "Aether-Guard-Data-Acquisition/1.0"

DEFAULT_FEEDS = [
    {
        "name": "aws-status",
        "url": "https://status.aws.amazon.com/rss/all.rss",
        "license": "AWS Site Terms",
        "license_url": "https://aws.amazon.com/terms/",
    },
    {
        "name": "gcp-status",
        "url": "https://status.cloud.google.com/en/feed.atom",
        "license": "Google Terms of Service",
        "license_url": "https://policies.google.com/terms",
    },
    {
        "name": "azure-status",
        "url": "https://status.azure.com/en-us/status/feed/",
        "license": "Microsoft Terms of Use",
        "license_url": "https://www.microsoft.com/legal/terms-of-use",
    },
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output-dir",
        default="Data/replay",
        help="Root output directory for incident archives and provenance metadata.",
    )
    parser.add_argument(
        "--feeds-file",
        default="",
        help="Optional JSON file with feed entries ({name,url,license,license_url}).",
    )
    parser.add_argument(
        "--max-items-per-feed",
        type=int,
        default=200,
        help="Maximum items to emit per feed.",
    )
    parser.add_argument(
        "--timeout-seconds",
        type=int,
        default=30,
        help="HTTP timeout for feed requests.",
    )
    return parser.parse_args()


def utc_now() -> datetime:
    return datetime.now(tz=timezone.utc)


def utc_now_iso() -> str:
    return utc_now().strftime("%Y-%m-%dT%H:%M:%SZ")


def run_id() -> str:
    return utc_now().strftime("%Y%m%dT%H%M%SZ")


def write_json(path: Path, payload: dict[str, Any]) -> None:
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")


def local_name(tag: str) -> str:
    if "}" in tag:
        return tag.rsplit("}", 1)[-1]
    if ":" in tag:
        return tag.rsplit(":", 1)[-1]
    return tag


def first_text(element: ET.Element, names: set[str]) -> str | None:
    for child in list(element):
        if local_name(child.tag) in names:
            text = "".join(child.itertext()).strip()
            if text:
                return text
    return None


def first_link(element: ET.Element) -> str | None:
    for child in list(element):
        if local_name(child.tag) != "link":
            continue
        href = child.attrib.get("href")
        if href:
            return href.strip()
        text = "".join(child.itertext()).strip()
        if text:
            return text
    return None


def strip_html(text: str) -> str:
    no_tags = re.sub(r"<[^>]+>", " ", text, flags=re.DOTALL)
    return " ".join(no_tags.split()).strip()


def normalize_timestamp(value: str | None) -> str | None:
    if not value:
        return None

    raw = value.strip()
    if not raw:
        return None

    try:
        parsed = datetime.fromisoformat(raw.replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            parsed = parsed.replace(tzinfo=timezone.utc)
        return parsed.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    except ValueError:
        pass

    try:
        parsed = parsedate_to_datetime(raw)
        if parsed.tzinfo is None:
            parsed = parsed.replace(tzinfo=timezone.utc)
        return parsed.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    except (TypeError, ValueError):
        return None


def load_feeds(path: str) -> list[dict[str, str]]:
    if not path:
        return list(DEFAULT_FEEDS)

    payload = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(payload, list):
        raise ValueError("Feeds file must be a JSON list.")

    normalized: list[dict[str, str]] = []
    for item in payload:
        if not isinstance(item, dict):
            raise ValueError("Feed entries must be objects.")
        name = str(item.get("name", "")).strip()
        url = str(item.get("url", "")).strip()
        license_name = str(item.get("license", "")).strip()
        license_url = str(item.get("license_url", "")).strip()
        if not name or not url:
            raise ValueError("Feed entries require name and url.")
        normalized.append(
            {
                "name": name,
                "url": url,
                "license": license_name or "Unknown",
                "license_url": license_url,
            }
        )
    return normalized


def parse_feed_items(
    xml_payload: str,
    feed_name: str,
    feed_url: str,
    max_items: int,
    fetched_at_utc: str,
) -> list[dict[str, Any]]:
    root = ET.fromstring(xml_payload)
    entries = [node for node in root.iter() if local_name(node.tag) in {"item", "entry"}]

    records: list[dict[str, Any]] = []
    for entry in entries:
        if len(records) >= max_items:
            break

        title = first_text(entry, {"title"})
        if not title:
            continue

        summary = first_text(entry, {"description", "summary", "content"}) or ""
        link = first_link(entry)
        guid = first_text(entry, {"guid", "id"})
        published_raw = first_text(entry, {"pubDate", "published", "updated", "date"})
        published_at = normalize_timestamp(published_raw)
        external_id = guid or link or title

        records.append(
            {
                "source": feed_name,
                "source_url": feed_url,
                "external_id": external_id,
                "title": title.strip(),
                "summary": strip_html(summary),
                "url": link,
                "published_at": published_at,
                "fetched_at_utc": fetched_at_utc,
            }
        )

    return records


def fetch_url(url: str, timeout_seconds: int) -> str:
    request = Request(url, headers={"User-Agent": USER_AGENT})
    with urlopen(request, timeout=timeout_seconds) as response:
        return response.read().decode("utf-8")


def write_jsonl(path: Path, rows: list[dict[str, Any]]) -> None:
    with path.open("w", encoding="utf-8") as handle:
        for row in rows:
            handle.write(json.dumps(row, ensure_ascii=False))
            handle.write("\n")


def main() -> int:
    args = parse_args()

    if args.max_items_per_feed <= 0:
        print("--max-items-per-feed must be > 0", file=sys.stderr)
        return 2

    try:
        feeds = load_feeds(args.feeds_file)
    except Exception as exc:
        print(f"Failed to load feeds: {exc}", file=sys.stderr)
        return 2

    output_root = Path(args.output_dir)
    incidents_dir = output_root / "incident_archives"
    provenance_dir = output_root / "provenance"
    incidents_dir.mkdir(parents=True, exist_ok=True)
    provenance_dir.mkdir(parents=True, exist_ok=True)

    feed_results: list[dict[str, Any]] = []
    total_records = 0
    success_count = 0

    stamp = run_id()
    fetched_at = utc_now_iso()

    for feed in feeds:
        name = feed["name"]
        url = feed["url"]
        output_path = incidents_dir / f"{name}_{stamp}.jsonl"

        try:
            xml_payload = fetch_url(url, args.timeout_seconds)
            records = parse_feed_items(
                xml_payload=xml_payload,
                feed_name=name,
                feed_url=url,
                max_items=args.max_items_per_feed,
                fetched_at_utc=fetched_at,
            )
            write_jsonl(output_path, records)
            feed_results.append(
                {
                    "name": name,
                    "url": url,
                    "license": feed.get("license", "Unknown"),
                    "license_url": feed.get("license_url", ""),
                    "status": "downloaded",
                    "record_count": len(records),
                    "output_jsonl": str(output_path),
                }
            )
            success_count += 1
            total_records += len(records)
            print(f"Fetched {len(records)} records from {name} -> {output_path}")
        except (ET.ParseError, HTTPError, URLError, TimeoutError, UnicodeDecodeError) as exc:
            feed_results.append(
                {
                    "name": name,
                    "url": url,
                    "license": feed.get("license", "Unknown"),
                    "license_url": feed.get("license_url", ""),
                    "status": "failed",
                    "error": str(exc),
                }
            )
            print(f"Failed {name}: {exc}", file=sys.stderr)

    manifest = {
        "dataset": "incident_archives",
        "generated_at_utc": utc_now_iso(),
        "command": " ".join(sys.argv),
        "feeds": feed_results,
        "summary": {
            "requested_feeds": len(feeds),
            "successful_feeds": success_count,
            "total_records": total_records,
        },
    }
    manifest_path = provenance_dir / "incident_archives_manifest.json"
    write_json(manifest_path, manifest)
    print(f"Wrote provenance manifest to {manifest_path}")

    if success_count == 0:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
