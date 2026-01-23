from __future__ import annotations

import json
import os
import threading
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path
from urllib.parse import quote, urlencode


ROOT = Path(__file__).resolve().parents[1]


def request_json(method: str, url: str, payload: dict | None = None) -> dict:
    data = None
    headers = {}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            body = response.read().decode("utf-8")
            return json.loads(body) if body else {}
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8")
        raise RuntimeError(f"{method} {url} failed: {exc.code} {body}") from exc


def get_json(url: str, params: dict[str, str] | None = None) -> dict:
    if params:
        url = f"{url}?{urlencode(params)}"
    return request_json("GET", url)


def request_bytes(method: str, url: str, body: bytes | None = None, headers: dict | None = None) -> bytes:
    req = urllib.request.Request(url, data=body, headers=headers or {}, method=method)
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            return response.read()
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8")
        raise RuntimeError(f"{method} {url} failed: {exc.code} {body}") from exc


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


def load_core_setting(key: str, fallback: str) -> str:
    appsettings = ROOT / "src/services/core-dotnet/AetherGuard.Core/appsettings.json"
    if not appsettings.exists():
        return fallback
    settings = json.loads(appsettings.read_text(encoding="utf-8"))
    value = settings.get(key, fallback)
    if not isinstance(value, str):
        return fallback
    if Path(value).is_absolute():
        return value
    return str(appsettings.parent / value)


def write_market_signal() -> Path:
    signal_path = os.getenv("AG_MARKET_SIGNAL_PATH") or load_core_setting(
        "MarketSignalPath", "Data/market_signal.json"
    )
    signal_file = Path(signal_path)
    signal_file.parent.mkdir(parents=True, exist_ok=True)
    signal_file.write_text(
        json.dumps({"rebalanceSignal": True, "timestamp": int(time.time())}),
        encoding="utf-8",
    )
    return signal_file


def register_agent(base_url: str, hostname: str) -> tuple[str, str]:
    response = request_json(
        "POST",
        f"{base_url}/api/v1/agent/register",
        {"hostname": hostname, "os": "Linux"},
    )
    agent_id = response.get("agentId")
    token = response.get("token")
    if not agent_id or not token:
        raise RuntimeError("Agent registration failed.")
    return agent_id, token


def heartbeat_loop(base_url: str, token: str, stop_event: threading.Event) -> threading.Thread:
    def run() -> None:
        while not stop_event.is_set():
            try:
                request_json(
                    "POST",
                    f"{base_url}/api/v1/agent/heartbeat",
                    {"token": token},
                )
            except Exception:
                pass
            stop_event.wait(5)

    thread = threading.Thread(target=run, daemon=True)
    thread.start()
    return thread


def wait_for_command(base_url: str, agent_id: str, action: str, timeout: int = 60) -> dict:
    deadline = time.time() + timeout
    while time.time() < deadline:
        response = get_json(f"{base_url}/poll", {"agentId": agent_id})
        commands = response.get("commands", [])
        for command in commands:
            if command.get("action") == action:
                return command
        time.sleep(2)
    raise RuntimeError(f"Timed out waiting for {action} command.")


def wait_for_command_any(base_url: str, agent_ids: list[str], action: str, timeout: int = 60) -> tuple[str, dict]:
    deadline = time.time() + timeout
    while time.time() < deadline:
        for agent_id in agent_ids:
            response = get_json(f"{base_url}/poll", {"agentId": agent_id})
            commands = response.get("commands", [])
            for command in commands:
                if command.get("action") == action:
                    return agent_id, command
        time.sleep(2)
    raise RuntimeError(f"Timed out waiting for {action} command.")


def upload_snapshot(base_url: str, workload_id: str, file_path: Path) -> None:
    body, boundary = build_multipart_body(file_path)
    upload_url = f"{base_url}/upload/{quote(workload_id)}"
    headers = {"Content-Type": f"multipart/form-data; boundary={boundary}"}
    request_bytes("POST", upload_url, body=body, headers=headers)


def send_feedback(base_url: str, agent_id: str, command_id: str, status: str, result: str = "") -> None:
    payload = {
        "agentId": agent_id,
        "commandId": command_id,
        "status": status,
        "result": result,
        "error": "",
    }
    request_json("POST", f"{base_url}/feedback", payload)


def verify_snapshot_on_disk(workload_id: str) -> Path:
    storage_root = os.getenv("AG_STORAGE_PATH") or load_core_setting("StoragePath", "Data/Snapshots")
    workload_dir = Path(storage_root) / workload_id
    if not workload_dir.exists():
        raise RuntimeError("Snapshot directory not found on disk.")
    snapshots = sorted(
        workload_dir.glob("*.tar.gz"),
        key=lambda path: path.stat().st_mtime,
        reverse=True,
    )
    if not snapshots:
        raise RuntimeError("Snapshot file not found on disk.")
    return snapshots[0]


def wait_for_audit(base_url: str, action: str, timeout: int = 30) -> None:
    deadline = time.time() + timeout
    while time.time() < deadline:
        audit_response = get_json(f"{base_url}/audits", {"action": action})
        if audit_response:
            return
        time.sleep(2)
    raise RuntimeError("Migration audit record not found.")


def main() -> int:
    base_url = os.getenv("AG_CORE_URL", "http://localhost:8080").rstrip("/")

    print("Starting fire drill...")
    signal_file = write_market_signal()
    print(f"Market signal set at {signal_file}")

    agent_a_id, agent_a_token = register_agent(base_url, "fire-drill-source")
    agent_b_id, agent_b_token = register_agent(base_url, "fire-drill-target")

    stop_event = threading.Event()
    heartbeat_loop(base_url, agent_a_token, stop_event)
    heartbeat_loop(base_url, agent_b_token, stop_event)

    print("Waiting for CHECKPOINT command...")
    source_id, checkpoint_cmd = wait_for_command_any(
        base_url,
        [agent_a_id, agent_b_id],
        "CHECKPOINT",
    )
    target_id = agent_b_id if source_id == agent_a_id else agent_a_id
    checkpoint_id = checkpoint_cmd.get("commandId")
    if not checkpoint_id:
        raise RuntimeError("CHECKPOINT command missing commandId.")

    dummy_path = ROOT / "dummy_snapshot.tar.gz"
    dummy_path.write_bytes(os.urandom(1024 * 1024))
    upload_snapshot(base_url, source_id, dummy_path)
    verify_snapshot_on_disk(source_id)
    send_feedback(base_url, source_id, checkpoint_id, "COMPLETED", "Snapshot uploaded")

    print("Waiting for RESTORE command...")
    restore_cmd = wait_for_command(base_url, target_id, "RESTORE")
    restore_id = restore_cmd.get("commandId")
    if not restore_id:
        raise RuntimeError("RESTORE command missing commandId.")

    parameters = restore_cmd.get("parameters", "{}")
    if isinstance(parameters, str):
        try:
            parameters = json.loads(parameters)
        except json.JSONDecodeError:
            parameters = {}
    snapshot_url = parameters.get("snapshotUrl") or parameters.get("downloadUrl")
    if not snapshot_url:
        raise RuntimeError("RESTORE command missing snapshotUrl.")

    request_bytes("GET", snapshot_url)
    send_feedback(base_url, target_id, restore_id, "COMPLETED", "Restore completed")

    wait_for_audit(base_url, "Migration Completed")

    stop_event.set()

    print("\033[32mSUCCESS: Full Migration Cycle Verified\033[0m")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
