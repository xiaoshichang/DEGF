from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path


SCRIPT_DIR = Path(__file__).resolve().parent
SERVER_DIR = SCRIPT_DIR.parent
ENGINE_DIR = SERVER_DIR / "Engine"
DEFAULT_CONFIG_PATH = SERVER_DIR / "Config" / "local-dev.json"
DEFAULT_EXE_CANDIDATES = (
    ENGINE_DIR / "build" / "engine" / "src" / "Debug" / "DEServer.exe",
    ENGINE_DIR / "build" / "engine" / "src" / "engine" / "Debug" / "DEServer.exe",
)
DEFAULT_PROCESS_KEYWORD = "DEServer"


def resolve_default_exe_path() -> Path:
    for candidate in DEFAULT_EXE_CANDIDATES:
        if candidate.exists():
            return candidate

    return DEFAULT_EXE_CANDIDATES[0]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Start the DE server cluster.")
    parser.add_argument(
        "--config",
        type=Path,
        default=DEFAULT_CONFIG_PATH,
        help="Path to the cluster config JSON file.",
    )
    parser.add_argument(
        "--exe",
        type=Path,
        default=resolve_default_exe_path(),
        help="Path to DEServer executable.",
    )
    return parser.parse_args()


def load_server_ids(config_path: Path) -> list[str]:
    data = json.loads(config_path.read_text(encoding="utf-8"))
    server_ids: list[str] = ["GM"]
    server_ids.extend(data.get("gate", {}).keys())
    server_ids.extend(data.get("game", {}).keys())
    return server_ids


def list_matching_processes(keyword: str) -> list[dict[str, str | int]]:
    excluded_names = {"powershell.exe", "python.exe", "py.exe", "cmd.exe"}

    if sys.platform == "win32":
        keyword_escaped = keyword.replace("'", "''")
        powershell = (
            f"$keyword = '{keyword_escaped}'; "
            "Get-CimInstance Win32_Process | "
            "Where-Object { ($_.Name -like \"*$keyword*\") -or "
            "(($_.CommandLine -ne $null) -and ($_.CommandLine -like \"*$keyword*\")) } | "
            "Select-Object ProcessId, Name, CommandLine | ConvertTo-Json -Compress"
        )
        completed = subprocess.run(
            ["powershell", "-NoProfile", "-Command", powershell],
            capture_output=True,
            text=True,
            check=False,
        )
        output = completed.stdout.strip()
        if completed.returncode != 0 or output == "" or output == "null":
            return []

        parsed = json.loads(output)
        if isinstance(parsed, dict):
            parsed = [parsed]

        processes: list[dict[str, str | int]] = []
        for item in parsed:
            name = str(item["Name"])
            command_line = str(item.get("CommandLine") or "")
            name_lower = name.lower()
            command_line_lower = command_line.lower()
            if name_lower in excluded_names:
                continue
            if name_lower == "deserver.exe" or keyword.lower() in command_line_lower:
                processes.append(
                    {
                        "pid": int(item["ProcessId"]),
                        "name": name,
                        "command_line": command_line,
                    }
                )

        return processes

    completed = subprocess.run(
        ["pgrep", "-af", keyword],
        capture_output=True,
        text=True,
        check=False,
    )
    if completed.returncode != 0:
        return []

    processes: list[dict[str, str | int]] = []
    for line in completed.stdout.splitlines():
        parts = line.strip().split(maxsplit=1)
        if not parts:
            continue
        processes.append(
            {
                "pid": int(parts[0]),
                "name": keyword,
                "command_line": parts[1] if len(parts) > 1 else "",
            }
        )
    return processes


def ensure_no_running_cluster(keyword: str) -> None:
    running = list_matching_processes(keyword)
    if not running:
        return

    details = ", ".join(f'{entry["name"]}({entry["pid"]})' for entry in running)
    raise RuntimeError(
        f"Existing server processes are already running: {details}. "
        "Run kill_server_cluster first."
    )


def start_cluster(config_path: Path, exe_path: Path) -> None:
    if not config_path.exists():
        raise FileNotFoundError(f"Config file not found: {config_path}")
    if not exe_path.exists():
        raise FileNotFoundError(f"DEServer executable not found: {exe_path}")

    ensure_no_running_cluster(DEFAULT_PROCESS_KEYWORD)
    server_ids = load_server_ids(config_path)

    started_processes: list[subprocess.Popen[str]] = []
    try:
        for server_id in server_ids:
            popen_kwargs: dict[str, object] = {
                "args": [str(exe_path), str(config_path), server_id],
                "cwd": str(ENGINE_DIR),
            }

            if sys.platform == "win32":
                detached_flags = subprocess.CREATE_NEW_PROCESS_GROUP | 0x00000008
                popen_kwargs["creationflags"] = detached_flags
                popen_kwargs["stdin"] = subprocess.DEVNULL
                popen_kwargs["stdout"] = subprocess.DEVNULL
                popen_kwargs["stderr"] = subprocess.DEVNULL

            process = subprocess.Popen(
                **popen_kwargs,
            )
            started_processes.append(process)
            print(f"Started {server_id} with PID {process.pid}")
            time.sleep(0.2)
    except Exception:
        for process in started_processes:
            subprocess.run(
                ["taskkill", "/PID", str(process.pid), "/T", "/F"],
                capture_output=True,
                text=True,
                check=False,
            )
        raise


def main() -> int:
    try:
        args = parse_args()
        start_cluster(args.config.resolve(), args.exe.resolve())
        return 0
    except Exception as exc:
        print(f"Failed to start cluster: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
