from __future__ import annotations

import json
import subprocess
import sys

PROCESS_KEYWORD = "DEServer"

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


def kill_pid(pid: int) -> None:
    completed = subprocess.run(
        ["taskkill", "/PID", str(pid), "/T", "/F"],
        capture_output=True,
        text=True,
        check=False,
    )
    if completed.returncode == 0:
        print(f"Killed PID {pid}")
        return

    combined_output = f"{completed.stdout}\n{completed.stderr}".strip()
    if "not found" in combined_output.lower() or "没有运行的任务" in combined_output:
        print(f"PID {pid} was already stopped")
        return

    raise RuntimeError(f"Failed to kill PID {pid}: {combined_output}")


def kill_cluster() -> None:
    processes = list_matching_processes(PROCESS_KEYWORD)
    if not processes:
        print(f"No running processes matched keyword: {PROCESS_KEYWORD}")
        return

    for entry in processes:
        pid = int(entry["pid"])
        print(f'Stopping {entry["name"]} (PID {pid})')
        kill_pid(pid)


def main() -> int:
    try:
        kill_cluster()
        return 0
    except Exception as exc:
        print(f"Failed to kill cluster: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
