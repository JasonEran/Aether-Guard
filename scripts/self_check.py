from __future__ import annotations

import argparse
import json
import os
import platform
import re
import socket
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
CORE_ROOT = ROOT / "src" / "services" / "core-dotnet" / "AetherGuard.Core"
APPSETTINGS_PATH = CORE_ROOT / "appsettings.json"

PORTS = [
    ("core-api", 5000),
    ("core-otel-grpc", 4317),
    ("core-otel-http", 4318),
    ("dashboard", 3000),
    ("ai-engine", 8000),
    ("postgres", 5432),
    ("rabbitmq", 5672),
    ("rabbitmq-management", 15672),
    ("redis", 6379),
    ("minio", 9000),
    ("minio-console", 9001),
    ("jaeger-ui", 16686),
]

STATUS_OK = "OK"
STATUS_WARN = "WARN"
STATUS_FAIL = "FAIL"

VERSION_RE = re.compile(r"\d+(?:\.\d+)+")


@dataclass
class CheckResult:
    status: str
    name: str
    detail: str


def run_command(command: list[str]) -> tuple[bool, str]:
    try:
        result = subprocess.run(
            command,
            check=False,
            capture_output=True,
            text=True,
        )
    except FileNotFoundError:
        return False, f"{command[0]} not found"
    output = (result.stdout or result.stderr).strip()
    if result.returncode != 0:
        return False, output or f"{command[0]} returned {result.returncode}"
    return True, output


def extract_version(text: str) -> str:
    match = VERSION_RE.search(text)
    return match.group(0) if match else ""


def parse_major(version_text: str) -> int | None:
    if not version_text:
        return None
    try:
        return int(version_text.split(".")[0])
    except ValueError:
        return None


def read_env(keys: list[str]) -> str | None:
    for key in keys:
        value = os.getenv(key)
        if value:
            return value
    return None


def get_setting(settings: dict, path: list[str], default: str | None = None) -> str | None:
    current = settings
    for key in path:
        if not isinstance(current, dict):
            return default
        if key not in current:
            return default
        current = current[key]
    if current is None:
        return default
    return str(current)


def check_tool(
    name: str,
    command: list[str],
    required: bool,
    detail_hint: str | None = None,
) -> CheckResult:
    ok, output = run_command(command)
    if not ok:
        status = STATUS_FAIL if required else STATUS_WARN
        detail = output or f"{command[0]} not available"
        if detail_hint:
            detail = f"{detail}. {detail_hint}"
        return CheckResult(status, name, detail)
    line = output.splitlines()[0] if output else "available"
    return CheckResult(STATUS_OK, name, line)


def check_dotnet(required: bool) -> CheckResult:
    ok, output = run_command(["dotnet", "--version"])
    if not ok:
        status = STATUS_FAIL if required else STATUS_WARN
        return CheckResult(status, "dotnet", output)
    version = extract_version(output) or output.strip()
    major = parse_major(version)
    if major is None:
        status = STATUS_FAIL if required else STATUS_WARN
        return CheckResult(status, "dotnet", f"unrecognized version: {output}")
    if major < 8:
        status = STATUS_FAIL if required else STATUS_WARN
        return CheckResult(status, "dotnet", f"version {version} (< 8)")
    return CheckResult(STATUS_OK, "dotnet", f"version {version}")


def check_node(required: bool) -> CheckResult:
    ok, output = run_command(["node", "--version"])
    if not ok:
        status = STATUS_FAIL if required else STATUS_WARN
        return CheckResult(status, "node", output)
    version = extract_version(output) or output.strip()
    return CheckResult(STATUS_OK, "node", f"version {version}")


def check_npm(required: bool) -> CheckResult:
    ok, output = run_command(["npm", "--version"])
    if not ok:
        status = STATUS_FAIL if required else STATUS_WARN
        return CheckResult(status, "npm", output)
    version = extract_version(output) or output.strip()
    return CheckResult(STATUS_OK, "npm", f"version {version}")


def check_docker_compose(required: bool) -> CheckResult:
    ok, output = run_command(["docker", "compose", "version"])
    if ok:
        line = output.splitlines()[0] if output else "docker compose available"
        return CheckResult(STATUS_OK, "docker compose", line)
    ok, output = run_command(["docker-compose", "--version"])
    if ok:
        line = output.splitlines()[0] if output else "docker-compose available"
        return CheckResult(STATUS_OK, "docker compose", line)
    status = STATUS_FAIL if required else STATUS_WARN
    return CheckResult(
        status,
        "docker compose",
        "docker compose plugin or docker-compose not found",
    )


def check_port(port: int) -> tuple[bool, str]:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            sock.bind(("127.0.0.1", port))
        except OSError as exc:
            return False, str(exc)
    return True, "available"


def check_writable_dir(path: Path) -> tuple[bool, str]:
    try:
        if path.exists() and not path.is_dir():
            return False, f"{path} exists and is not a directory"
        path.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(
            dir=path,
            prefix=".self_check_",
            delete=True,
        ) as temp_file:
            temp_file.write(b"ok")
        return True, f"writable: {path}"
    except Exception as exc:
        return False, f"{path} not writable ({exc})"


def load_settings() -> tuple[dict | None, str | None]:
    if not APPSETTINGS_PATH.exists():
        return None, f"missing {APPSETTINGS_PATH}"
    try:
        return json.loads(APPSETTINGS_PATH.read_text(encoding="utf-8")), None
    except json.JSONDecodeError as exc:
        return None, f"invalid JSON in {APPSETTINGS_PATH}: {exc}"


def resolve_provider(settings: dict) -> str:
    env_provider = read_env(["SnapshotStorage__Provider", "SNAPSHOTSTORAGE__PROVIDER"])
    provider = env_provider or get_setting(settings, ["SnapshotStorage", "Provider"]) or "Local"
    return provider.strip()


def resolve_local_path(settings: dict) -> Path:
    local_path = read_env(["SnapshotStorage__LocalPath", "SNAPSHOTSTORAGE__LOCALPATH"])
    storage_path = read_env(["StoragePath", "STORAGEPATH"])
    configured = (
        local_path
        or get_setting(settings, ["SnapshotStorage", "LocalPath"])
        or storage_path
        or get_setting(settings, ["StoragePath"])
        or "Data/Snapshots"
    )
    resolved = Path(configured)
    if resolved.is_absolute():
        return resolved
    return CORE_ROOT / resolved


def resolve_market_signal_path(settings: dict) -> Path:
    env_path = read_env(["MarketSignalPath", "MARKETSIGNALPATH"])
    configured = env_path or get_setting(settings, ["MarketSignalPath"]) or "Data/market_signal.json"
    resolved = Path(configured)
    if resolved.is_absolute():
        return resolved
    return CORE_ROOT / resolved


def resolve_s3_value(settings: dict, env_keys: list[str], path: list[str]) -> str | None:
    env_value = read_env(env_keys)
    if env_value:
        return env_value
    return get_setting(settings, path)


def run_self_check(args: argparse.Namespace) -> list[CheckResult]:
    results: list[CheckResult] = []
    python_version = f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"
    results.append(CheckResult(STATUS_OK, "python", f"version {python_version}"))

    if not args.skip_tools:
        if args.target == "docker":
            results.append(check_tool("docker", ["docker", "--version"], True))
            results.append(check_docker_compose(True))
        else:
            results.append(check_tool("docker", ["docker", "--version"], False))
            results.append(check_docker_compose(False))

        if args.target == "local":
            results.append(check_dotnet(True))
            results.append(check_node(True))
            results.append(check_npm(True))
        else:
            results.append(check_dotnet(False))
            results.append(check_node(False))
            results.append(check_npm(False))

        cmake_required = args.target == "local"
        results.append(check_tool("cmake", ["cmake", "--version"], cmake_required))
        results.append(check_tool("ninja", ["ninja", "--version"], False, "optional, faster C++ builds"))
        results.append(check_tool("nasm", ["nasm", "-v"], False, "optional, required for some Windows builds"))

        if platform.system().lower() == "linux":
            results.append(check_tool("criu", ["criu", "--version"], False, "optional, enables real checkpoints"))

    if not args.skip_ports:
        for name, port in PORTS:
            free, detail = check_port(port)
            if free:
                results.append(CheckResult(STATUS_OK, f"port {name}", f"{port} available"))
            else:
                status = STATUS_WARN if args.allow_port_in_use else STATUS_FAIL
                results.append(
                    CheckResult(
                        status,
                        f"port {name}",
                        f"{port} in use or restricted ({detail})",
                    )
                )

    settings, error = load_settings()
    if settings is None:
        results.append(CheckResult(STATUS_FAIL, "appsettings", error or "missing settings"))
        return results

    provider = resolve_provider(settings).lower()
    results.append(CheckResult(STATUS_OK, "snapshot provider", provider))

    if provider == "s3":
        bucket = resolve_s3_value(
            settings,
            ["SnapshotStorage__S3__Bucket", "SNAPSHOTSTORAGE__S3__BUCKET"],
            ["SnapshotStorage", "S3", "Bucket"],
        )
        endpoint = resolve_s3_value(
            settings,
            ["SnapshotStorage__S3__Endpoint", "SNAPSHOTSTORAGE__S3__ENDPOINT"],
            ["SnapshotStorage", "S3", "Endpoint"],
        )
        access_key = resolve_s3_value(
            settings,
            ["SnapshotStorage__S3__AccessKey", "SNAPSHOTSTORAGE__S3__ACCESSKEY"],
            ["SnapshotStorage", "S3", "AccessKey"],
        )
        secret_key = resolve_s3_value(
            settings,
            ["SnapshotStorage__S3__SecretKey", "SNAPSHOTSTORAGE__S3__SECRETKEY"],
            ["SnapshotStorage", "S3", "SecretKey"],
        )
        missing = [
            name
            for name, value in [
                ("bucket", bucket),
                ("endpoint", endpoint),
                ("access key", access_key),
                ("secret key", secret_key),
            ]
            if not value
        ]
        if missing:
            results.append(CheckResult(STATUS_FAIL, "snapshot s3 config", f"missing {', '.join(missing)}"))
        else:
            results.append(
                CheckResult(
                    STATUS_OK,
                    "snapshot s3 config",
                    f"bucket {bucket}, endpoint {endpoint}",
                )
            )
    else:
        storage_path = resolve_local_path(settings)
        ok, detail = check_writable_dir(storage_path)
        status = STATUS_OK if ok else STATUS_FAIL
        results.append(CheckResult(status, "snapshot storage", detail))

    market_signal_path = resolve_market_signal_path(settings)
    ok, detail = check_writable_dir(market_signal_path.parent)
    status = STATUS_OK if ok else STATUS_FAIL
    results.append(CheckResult(status, "market signal path", detail))

    return results


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Aether-Guard self-check: dependencies, ports, and permissions.",
    )
    parser.add_argument(
        "--target",
        choices=["docker", "local"],
        default="docker",
        help="Select dependency profile (docker or local).",
    )
    parser.add_argument(
        "--allow-port-in-use",
        action="store_true",
        help="Treat occupied ports as warnings instead of failures.",
    )
    parser.add_argument(
        "--skip-ports",
        action="store_true",
        help="Skip port availability checks.",
    )
    parser.add_argument(
        "--skip-tools",
        action="store_true",
        help="Skip toolchain and dependency checks.",
    )
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    results = run_self_check(args)
    for result in results:
        print(f"[{result.status}] {result.name}: {result.detail}")

    failures = sum(1 for result in results if result.status == STATUS_FAIL)
    warnings = sum(1 for result in results if result.status == STATUS_WARN)
    oks = len(results) - failures - warnings
    print(f"Summary: OK={oks} WARN={warnings} FAIL={failures}")

    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
