from __future__ import annotations

import argparse
import json
from pathlib import Path


SCRIPT_DIR = Path(__file__).resolve().parent
ROOT = SCRIPT_DIR.parent

MARKET_SIGNAL_PATH = (
    ROOT
    / "src"
    / "services"
    / "core-dotnet"
    / "AetherGuard.Core"
    / "Data"
    / "market_signal.json"
)
SPOT_PRICES_PATH = (
    ROOT
    / "src"
    / "services"
    / "ai-engine"
    / "Data"
    / "spot_prices.json"
)

BANNER = r"""
   ___      _           ___        _ _ _
  / __|__ _| |___ _ _  | _ )_  _ __| | | |
 | (_ / _` | / -_) '_| | _ \ || / _` | | |
  \___\__,_|_\___|_|   |___/\_,_\__,_|_|_|
"""

COLOR_RESET = "\033[0m"
COLOR_RED_BOLD = "\033[1;31m"
COLOR_GREEN = "\033[32m"
COLOR_CYAN = "\033[36m"


def write_json(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2), encoding="utf-8")


def print_header(title: str) -> None:
    print(BANNER)
    print(f"{COLOR_CYAN}== {title} =={COLOR_RESET}")


def inject_crisis() -> None:
    print_header("FIRE DRILL: CRISIS MODE")
    print(f"{COLOR_RED_BOLD}[!!!] INJECTING MARKET CRASH SIMULATION...{COLOR_RESET}")

    write_json(MARKET_SIGNAL_PATH, {"rebalanceSignal": True})
    volatile_prices = [1.0, 1.1, 1.2, 5.0, 8.5]
    write_json(SPOT_PRICES_PATH, volatile_prices)

    print(f"[WRITE] market_signal.json -> {MARKET_SIGNAL_PATH}")
    print(f"[WRITE] spot_prices.json  -> {SPOT_PRICES_PATH}")
    print("[STATUS] Crisis injected. Orchestrator should respond immediately.")


def reset_system() -> None:
    print_header("FIRE DRILL: RESET MODE")
    print(f"{COLOR_GREEN}[OK] System Stabilizing...{COLOR_RESET}")

    write_json(MARKET_SIGNAL_PATH, {"rebalanceSignal": False})
    stable_prices = [1.0, 1.01, 0.99, 1.0]
    write_json(SPOT_PRICES_PATH, stable_prices)

    print(f"[WRITE] market_signal.json -> {MARKET_SIGNAL_PATH}")
    print(f"[WRITE] spot_prices.json  -> {SPOT_PRICES_PATH}")
    print("[STATUS] System reset. Ready for next demo.")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Aether-Guard Fire Drill Control: inject crisis or reset.",
    )
    parser.add_argument(
        "command",
        choices=["start", "stop", "reset"],
        help="start=inject crisis, stop/reset=return to stable state",
    )
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    if args.command == "start":
        inject_crisis()
        return 0

    reset_system()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
