from __future__ import annotations

import json
import os
import shutil
import subprocess
import urllib.error
import urllib.request
from urllib.parse import urlencode


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


def fetch_audit_actions(command_id: str) -> list[str]:
    connection = (
        os.getenv("AG_DB_CONNECTION")
        or os.getenv("ConnectionStrings__DefaultConnection")
        or os.getenv("DATABASE_URL")
        or os.getenv("PG_CONN_STR")
    )
    if not connection:
        raise RuntimeError("Database connection string not set in AG_DB_CONNECTION or ConnectionStrings__DefaultConnection.")

    try:
        import psycopg2  # type: ignore
    except Exception:
        psycopg2 = None

    if psycopg2:
        with psycopg2.connect(connection) as conn:
            with conn.cursor() as cur:
                cur.execute(
                    "SELECT action FROM command_audits WHERE command_id = %s",
                    (command_id,),
                )
                return [row[0] for row in cur.fetchall()]

    psql = shutil.which("psql")
    if psql:
        query = f"SELECT action FROM command_audits WHERE command_id = '{command_id}';"
        result = subprocess.run(
            [psql, connection, "-At", "-c", query],
            check=True,
            capture_output=True,
            text=True,
        )
        return [line.strip() for line in result.stdout.splitlines() if line.strip()]

    raise RuntimeError("psycopg2 or psql is required to query command_audits.")


def main() -> int:
    base_url = os.getenv("AG_CORE_URL", "http://localhost:8080").rstrip("/")

    register = request_json(
        "POST",
        f"{base_url}/api/v1/agent/register",
        {"hostname": "verify-agent", "os": "Linux"},
    )
    agent_id = register.get("agentId")
    if not agent_id:
        print("Agent registration failed: missing agentId")
        return 1

    queue_payload = {
        "workloadId": agent_id,
        "action": "MIGRATE",
        "params": {"targetIp": "10.0.0.1"},
    }
    queued = request_json("POST", f"{base_url}/commands/queue", queue_payload)
    command_id = queued.get("commandId")
    if not command_id:
        print("Command queue failed: missing commandId")
        return 1

    poll = get_json(f"{base_url}/poll", {"agentId": agent_id})
    commands = poll.get("commands", [])
    if not commands:
        print("Poll returned no commands")
        return 1

    command = next((item for item in commands if str(item.get("commandId")) == str(command_id)), None)
    if not command:
        print("Queued command not found in poll response")
        return 1

    if not command.get("signature") or not command.get("nonce"):
        print("Polled command missing signature or nonce")
        return 1

    feedback_payload = {
        "agentId": agent_id,
        "commandId": command_id,
        "status": "COMPLETED",
        "result": "Success",
        "error": "",
    }
    request_json("POST", f"{base_url}/feedback", feedback_payload)

    actions = fetch_audit_actions(str(command_id))
    required = {"Command Queued", "Execution Result Received"}
    if not required.issubset(set(actions)):
        print(f"Audit entries missing. Found actions: {actions}")
        return 1

    print("Phase 2 verification passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
