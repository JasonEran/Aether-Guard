from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent


def check_lifecycle_manager(errors: list[str]) -> None:
    lifecycle_path = ROOT / "src/services/agent-cpp/LifecycleManager.hpp"
    if not lifecycle_path.exists():
        errors.append(f"Missing LifecycleManager.hpp at {lifecycle_path}")
        return

    content = lifecycle_path.read_text(encoding="utf-8")
    if not re.search(r"\bThaw\s*\(", content):
        errors.append("LifecycleManager missing Thaw method declaration.")


def check_agent_command_schema(errors: list[str]) -> None:
    command_path = ROOT / "src/services/core-dotnet/AetherGuard.Core/models/AgentCommand.cs"
    if not command_path.exists():
        errors.append(f"Missing AgentCommand.cs at {command_path}")
        return

    content = command_path.read_text(encoding="utf-8")
    property_pattern = re.compile(r"public\s+[\w<>\[\]\?]+\s+(\w+)\s*\{")
    properties = property_pattern.findall(content)

    if "Nonce" not in properties or "Signature" not in properties:
        errors.append("AgentCommand missing Nonce or Signature properties.")
        return

    payload = {camel_case(name): "value" for name in properties}
    payload_json = json.dumps(payload)
    if "nonce" not in payload or "signature" not in payload:
        errors.append("AgentCommand JSON payload missing nonce or signature.")
        return

    if "\"nonce\"" not in payload_json or "\"signature\"" not in payload_json:
        errors.append("AgentCommand JSON serialization missing nonce or signature.")


def check_risk_scorer(errors: list[str]) -> None:
    ai_engine_path = ROOT / "src/services/ai-engine"
    sys.path.insert(0, str(ai_engine_path))
    try:
        from model import RiskScorer  # type: ignore
    except Exception as exc:  # pragma: no cover - safety net for import errors
        errors.append(f"Failed to import RiskScorer: {exc}")
        return

    scorer = RiskScorer()
    assessment = scorer.assess_risk([], True, 0.0)
    if getattr(assessment, "Priority", None) != "CRITICAL":
        errors.append("RiskScorer did not return CRITICAL priority on rebalance signal.")


def check_agent_build(errors: list[str]) -> None:
    agent_dir = ROOT / "src/services/agent-cpp"
    build_dir = agent_dir / "build_blueprint_v1"
    build_dir.mkdir(parents=True, exist_ok=True)

    try:
        subprocess.run(
            ["cmake", "-S", str(agent_dir), "-B", str(build_dir)],
            check=True,
            capture_output=True,
            text=True,
        )
        subprocess.run(
            ["cmake", "--build", str(build_dir)],
            check=True,
            capture_output=True,
            text=True,
        )
    except FileNotFoundError:
        errors.append("CMake is not available on PATH.")
    except subprocess.CalledProcessError as exc:
        output = exc.stdout or ""
        error_output = exc.stderr or ""
        errors.append(
            "CMake build failed:\n"
            + output
            + ("\n" if output and error_output else "")
            + error_output
        )


def camel_case(value: str) -> str:
    if not value:
        return value
    return value[0].lower() + value[1:]


def main() -> int:
    errors: list[str] = []

    check_lifecycle_manager(errors)
    check_agent_command_schema(errors)
    check_risk_scorer(errors)
    check_agent_build(errors)

    if errors:
        print("Blueprint v1 verification failed.")
        for error in errors:
            print(f"- {error}")
        return 1

    print("Blueprint v1 verification passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
