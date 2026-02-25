#!/usr/bin/env python3
"""Collect spot price data for v2.3 replay/backtesting."""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

SPOT_JS_URL = "https://spot-price.s3.amazonaws.com/spot.js"
AWS_TERMS_URL = "https://aws.amazon.com/terms/"
USER_AGENT = "Aether-Guard-Data-Acquisition/1.0"
CSV_FIELDS = [
    "timestamp_utc",
    "region",
    "availability_zone",
    "instance_type",
    "instance_family",
    "operating_system",
    "currency",
    "spot_price_usd",
    "product_description",
    "source",
    "source_url",
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--source",
        choices=("spot-js", "ec2-api"),
        default="spot-js",
        help="Data source mode. Use ec2-api for historical pulls with AWS credentials.",
    )
    parser.add_argument(
        "--output-dir",
        default="Data/replay",
        help="Root output directory for generated CSV and provenance metadata.",
    )
    parser.add_argument(
        "--regions",
        default="",
        help="Comma-separated region allowlist. Empty means all regions in source.",
    )
    parser.add_argument(
        "--instance-types",
        default="",
        help="Comma-separated instance type allowlist. Empty means all instance types.",
    )
    parser.add_argument(
        "--region",
        default="us-east-1",
        help="Single AWS region for ec2-api mode.",
    )
    parser.add_argument(
        "--product-descriptions",
        default="Linux/UNIX",
        help="Comma-separated EC2 product descriptions for ec2-api mode.",
    )
    parser.add_argument(
        "--start-time",
        default="",
        help="UTC start time for ec2-api mode in ISO-8601 (for example 2026-01-01T00:00:00Z).",
    )
    parser.add_argument(
        "--end-time",
        default="",
        help="UTC end time for ec2-api mode in ISO-8601.",
    )
    parser.add_argument(
        "--max-records",
        type=int,
        default=500_000,
        help="Maximum rows to emit before truncating.",
    )
    parser.add_argument(
        "--timeout-seconds",
        type=int,
        default=30,
        help="HTTP timeout in seconds.",
    )
    return parser.parse_args()


def utc_now() -> datetime:
    return datetime.now(tz=timezone.utc)


def format_utc(value: datetime) -> str:
    return value.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def run_id() -> str:
    return utc_now().strftime("%Y%m%dT%H%M%SZ")


def parse_csv_list(value: str) -> set[str]:
    return {item.strip() for item in value.split(",") if item.strip()}


def parse_iso8601(value: str) -> datetime:
    normalized = value.strip().replace("Z", "+00:00")
    parsed = datetime.fromisoformat(normalized)
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def parse_price(raw_value: Any) -> float | None:
    try:
        return float(raw_value)
    except (TypeError, ValueError):
        return None


def parse_spot_js_payload(payload: str) -> dict[str, Any]:
    match = re.search(r"callback\((.*)\)\s*;?\s*$", payload, flags=re.DOTALL)
    if not match:
        raise ValueError("Unexpected spot.js format: callback(...) wrapper not found.")
    return json.loads(match.group(1))


def fetch_spot_js(timeout_seconds: int) -> tuple[dict[str, Any], str | None]:
    request = Request(SPOT_JS_URL, headers={"User-Agent": USER_AGENT})
    with urlopen(request, timeout=timeout_seconds) as response:
        payload = response.read().decode("utf-8")
        last_modified = response.headers.get("Last-Modified")
    return parse_spot_js_payload(payload), last_modified


def collect_from_spot_js(
    timeout_seconds: int,
    regions: set[str],
    instance_types: set[str],
    max_records: int,
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    data, last_modified = fetch_spot_js(timeout_seconds)
    rows: list[dict[str, Any]] = []
    snapshot_time = format_utc(utc_now())

    for region_data in data.get("config", {}).get("regions", []):
        region = str(region_data.get("region", "")).strip()
        if regions and region not in regions:
            continue

        for family_data in region_data.get("instanceTypes", []):
            family = str(family_data.get("type", "")).strip()
            for size_data in family_data.get("sizes", []):
                instance_type = str(size_data.get("size", "")).strip()
                if instance_types and instance_type not in instance_types:
                    continue

                for value_column in size_data.get("valueColumns", []):
                    operating_system = str(value_column.get("name", "unknown")).strip()
                    for currency, raw_price in value_column.get("prices", {}).items():
                        price = parse_price(raw_price)
                        row = {
                            "timestamp_utc": snapshot_time,
                            "region": region,
                            "availability_zone": "",
                            "instance_type": instance_type,
                            "instance_family": family,
                            "operating_system": operating_system,
                            "currency": currency,
                            "spot_price_usd": price if currency == "USD" else None,
                            "product_description": operating_system,
                            "source": "aws_spot_js_snapshot",
                            "source_url": SPOT_JS_URL,
                        }
                        rows.append(row)
                        if len(rows) >= max_records:
                            metadata = {
                                "mode": "spot-js",
                                "source_url": SPOT_JS_URL,
                                "last_modified": last_modified,
                                "spot_js_version": data.get("vers"),
                                "truncated": True,
                            }
                            return rows, metadata

    metadata = {
        "mode": "spot-js",
        "source_url": SPOT_JS_URL,
        "last_modified": last_modified,
        "spot_js_version": data.get("vers"),
        "truncated": False,
    }
    return rows, metadata


def collect_from_ec2_api(
    region: str,
    product_descriptions: list[str],
    instance_types: set[str],
    start_time: datetime,
    end_time: datetime,
    max_records: int,
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    try:
        import boto3  # type: ignore[import-untyped]
    except Exception as exc:  # pragma: no cover - optional dependency
        raise RuntimeError(
            "ec2-api mode requires boto3. Install with: pip install boto3"
        ) from exc

    client = boto3.client("ec2", region_name=region)
    request_kwargs: dict[str, Any] = {
        "StartTime": start_time,
        "EndTime": end_time,
        "ProductDescriptions": product_descriptions,
        "MaxResults": 1000,
    }
    if instance_types:
        request_kwargs["InstanceTypes"] = sorted(instance_types)

    rows: list[dict[str, Any]] = []
    next_token = None

    while True:
        if next_token:
            request_kwargs["NextToken"] = next_token
        elif "NextToken" in request_kwargs:
            request_kwargs.pop("NextToken", None)

        response = client.describe_spot_price_history(**request_kwargs)
        for item in response.get("SpotPriceHistory", []):
            instance_type = str(item.get("InstanceType", "")).strip()
            row = {
                "timestamp_utc": format_utc(item["Timestamp"]),
                "region": region,
                "availability_zone": item.get("AvailabilityZone", ""),
                "instance_type": instance_type,
                "instance_family": instance_type.split(".")[0] if "." in instance_type else "",
                "operating_system": item.get("ProductDescription", ""),
                "currency": "USD",
                "spot_price_usd": parse_price(item.get("SpotPrice")),
                "product_description": item.get("ProductDescription", ""),
                "source": "aws_ec2_spot_price_history_api",
                "source_url": "https://docs.aws.amazon.com/AWSEC2/latest/APIReference/API_DescribeSpotPriceHistory.html",
            }
            rows.append(row)
            if len(rows) >= max_records:
                metadata = {
                    "mode": "ec2-api",
                    "truncated": True,
                    "region": region,
                    "start_time_utc": format_utc(start_time),
                    "end_time_utc": format_utc(end_time),
                    "request_product_descriptions": product_descriptions,
                }
                return rows, metadata

        next_token = response.get("NextToken")
        if not next_token:
            break

    metadata = {
        "mode": "ec2-api",
        "truncated": False,
        "region": region,
        "start_time_utc": format_utc(start_time),
        "end_time_utc": format_utc(end_time),
        "request_product_descriptions": product_descriptions,
    }
    return rows, metadata


def write_rows_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=CSV_FIELDS)
        writer.writeheader()
        for row in rows:
            writer.writerow(row)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")


def main() -> int:
    args = parse_args()

    if args.max_records <= 0:
        print("--max-records must be > 0.", file=sys.stderr)
        return 2

    output_root = Path(args.output_dir)
    spot_dir = output_root / "spot_history"
    provenance_dir = output_root / "provenance"
    spot_dir.mkdir(parents=True, exist_ok=True)
    provenance_dir.mkdir(parents=True, exist_ok=True)

    regions = parse_csv_list(args.regions)
    instance_types = parse_csv_list(args.instance_types)
    command = " ".join(sys.argv)

    try:
        if args.source == "spot-js":
            rows, metadata = collect_from_spot_js(
                timeout_seconds=args.timeout_seconds,
                regions=regions,
                instance_types=instance_types,
                max_records=args.max_records,
            )
        else:
            end_time = parse_iso8601(args.end_time) if args.end_time else utc_now()
            start_time = parse_iso8601(args.start_time) if args.start_time else end_time - timedelta(days=30)
            product_descriptions = sorted(parse_csv_list(args.product_descriptions)) or ["Linux/UNIX"]
            rows, metadata = collect_from_ec2_api(
                region=args.region,
                product_descriptions=product_descriptions,
                instance_types=instance_types,
                start_time=start_time,
                end_time=end_time,
                max_records=args.max_records,
            )
    except (HTTPError, URLError, TimeoutError, RuntimeError, ValueError) as exc:
        print(f"Spot data collection failed: {exc}", file=sys.stderr)
        return 1
    except Exception as exc:  # pragma: no cover - safety net
        print(f"Unexpected spot data collection failure: {exc}", file=sys.stderr)
        return 1

    if not rows:
        print("No spot data rows collected.", file=sys.stderr)
        return 1

    stamp = run_id()
    csv_path = spot_dir / f"spot_history_{args.source}_{stamp}.csv"
    write_rows_csv(csv_path, rows)

    manifest = {
        "dataset": "spot_history",
        "generated_at_utc": format_utc(utc_now()),
        "command": command,
        "record_count": len(rows),
        "output_csv": str(csv_path),
        "license": "AWS Site Terms",
        "license_url": AWS_TERMS_URL,
        "metadata": metadata,
    }
    manifest_path = provenance_dir / "spot_history_manifest.json"
    write_json(manifest_path, manifest)

    print(f"Wrote {len(rows)} rows to {csv_path}")
    print(f"Wrote provenance manifest to {manifest_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
