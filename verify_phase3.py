from __future__ import annotations

import hashlib
import json
import os
import subprocess
import urllib.error
import urllib.request
import uuid
from pathlib import Path
from urllib.parse import quote


ROOT = Path(__file__).resolve().parent


def build_multipart_body(file_path: Path) -> tuple[bytes, str]:
    boundary = f"----AetherGuardBoundary{uuid.uuid4().hex}"
    file_bytes = file_path.read_bytes()

    parts = [
        f"--{boundary}\r\n".encode("utf-8"),
        (
            f'Content-Disposition: form-data; name="file"; '
            f'filename="{file_path.name}"\r\n'
        ).encode("utf-8"),
        b"Content-Type: application/gzip\r\n\r\n",
        file_bytes,
        b"\r\n",
        f"--{boundary}--\r\n".encode("utf-8"),
    ]

    return b"".join(parts), boundary


def request_bytes(method: str, url: str, body: bytes | None = None, headers: dict | None = None) -> bytes:
    req = urllib.request.Request(url, data=body, headers=headers or {}, method=method)
    try:
        with urllib.request.urlopen(req, timeout=15) as response:
            return response.read()
    except urllib.error.HTTPError as exc:
        error_body = exc.read().decode("utf-8")
        raise RuntimeError(f"{method} {url} failed: {exc.code} {error_body}") from exc


def resolve_storage_root() -> Path:
    storage_override = os.getenv("AG_STORAGE_PATH")
    if storage_override:
        return Path(storage_override)

    appsettings = ROOT / "src/services/core-dotnet/AetherGuard.Core/appsettings.json"
    if not appsettings.exists():
        raise RuntimeError("appsettings.json not found to resolve StoragePath.")

    settings = json.loads(appsettings.read_text(encoding="utf-8"))
    storage_path = settings.get("StoragePath", "Data/Snapshots")
    storage = Path(storage_path)
    if storage.is_absolute():
        return storage

    return appsettings.parent / storage


def resolve_storage_mode() -> str:
    env_provider = os.getenv("AG_STORAGE_PROVIDER")
    if env_provider:
        return env_provider.strip().lower()

    appsettings = ROOT / "src/services/core-dotnet/AetherGuard.Core/appsettings.json"
    if not appsettings.exists():
        return "local"

    settings = json.loads(appsettings.read_text(encoding="utf-8"))
    snapshot_storage = settings.get("SnapshotStorage", {})
    provider = snapshot_storage.get("Provider", "")
    if provider:
        return str(provider).strip().lower()

    s3_bucket = snapshot_storage.get("S3", {}).get("Bucket", "")
    if s3_bucket:
        return "s3"

    return "local"


def md5_digest(data: bytes) -> str:
    return hashlib.md5(data).hexdigest()


def run_command(command: list[str], workdir: Path) -> None:
    try:
        subprocess.run(
            command,
            cwd=workdir,
            check=True,
            capture_output=True,
            text=True,
        )
    except FileNotFoundError as exc:
        raise RuntimeError(f"{command[0]} not found on PATH.") from exc
    except subprocess.CalledProcessError as exc:
        stdout = exc.stdout or ""
        stderr = exc.stderr or ""
        raise RuntimeError(
            f"Command failed: {' '.join(command)}\n{stdout}\n{stderr}".strip()
        ) from exc


def build_and_test_agent() -> None:
    agent_dir = ROOT / "src/services/agent-cpp"
    build_dir = agent_dir / "build_phase3"
    build_dir.mkdir(parents=True, exist_ok=True)

    run_command(["cmake", "-S", str(agent_dir), "-B", str(build_dir)], ROOT)
    run_command(["cmake", "--build", str(build_dir)], ROOT)

    try:
        run_command(["ctest", "--test-dir", str(build_dir), "--output-on-failure"], ROOT)
    except RuntimeError:
        test_binary = build_dir / "AetherAgentTests"
        if os.name == "nt":
            test_binary = test_binary.with_suffix(".exe")
        if not test_binary.exists():
            raise
        run_command([str(test_binary)], ROOT)


def main() -> int:
    base_url = os.getenv("AG_CORE_URL", "http://localhost:8080").rstrip("/")
    workload_id = "verify-workload"

    dummy_path = ROOT / "dummy_snapshot.tar.gz"
    dummy_path.write_bytes(os.urandom(1024 * 1024))
    original_bytes = dummy_path.read_bytes()
    original_md5 = md5_digest(original_bytes)

    body, boundary = build_multipart_body(dummy_path)
    upload_url = f"{base_url}/upload/{quote(workload_id)}"
    headers = {"Content-Type": f"multipart/form-data; boundary={boundary}"}
    request_bytes("POST", upload_url, body=body, headers=headers)

    storage_mode = resolve_storage_mode()
    if storage_mode != "s3":
        storage_root = resolve_storage_root()
        workload_dir = storage_root / workload_id
        if not workload_dir.exists():
            print("Upload verification failed: workload directory not found.")
            return 1

        stored_files = sorted(
            workload_dir.glob("*.tar.gz"),
            key=lambda path: path.stat().st_mtime,
            reverse=True,
        )
        if not stored_files:
            print("Upload verification failed: snapshot file not found.")
            return 1

        stored_file = stored_files[0]
        if stored_file.stat().st_size != len(original_bytes):
            print("Upload verification failed: stored file size mismatch.")
            return 1

    download_url = f"{base_url}/download/{quote(workload_id)}"
    downloaded_bytes = request_bytes("GET", download_url)
    downloaded_md5 = md5_digest(downloaded_bytes)

    if downloaded_md5 != original_md5:
        print("Download verification failed: checksum mismatch.")
        return 1

    build_and_test_agent()

    print("Phase 3 verification passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
