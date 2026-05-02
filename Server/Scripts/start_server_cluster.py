from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
import shutil
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
GM_SERVER_ID = "GM"
WINDOW_LAYOUT_ROWS = 2
WINDOW_LAYOUT_COLUMNS = 3


def make_window_title(server_id: str) -> str:
    return f"DEGF-{server_id}"


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
    server_ids: list[str] = [GM_SERVER_ID]
    server_ids.extend(data.get("gate", {}).keys())
    server_ids.extend(data.get("game", {}).keys())
    return server_ids


def arrange_windows_with_powershell(server_ids: list[str]) -> None:
    if sys.platform != "win32":
        return

    script = r"""
Add-Type @"
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
public static class Win32WindowTools {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public static string GetTitle(IntPtr hWnd) {
        int length = GetWindowTextLength(hWnd);
        if (length <= 0) {
            return "";
        }

        StringBuilder builder = new StringBuilder(length + 1);
        GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    public static IntPtr FindWindowByTitle(string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) {
            if (!IsWindowVisible(hWnd)) {
                return true;
            }

            string windowTitle = GetTitle(hWnd);
            if (windowTitle.Equals(title, StringComparison.OrdinalIgnoreCase) || windowTitle.Contains(title)) {
                found = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@
$layout = $env:DEGF_WINDOW_LAYOUT | ConvertFrom-Json
$rows = [int]$env:DEGF_WINDOW_ROWS
$columns = [int]$env:DEGF_WINDOW_COLUMNS
$screenWidth = [Win32WindowTools]::GetSystemMetrics(0)
$screenHeight = [Win32WindowTools]::GetSystemMetrics(1)
$cellWidth = [Math]::Max(320, [Math]::Floor($screenWidth / $columns))
$cellHeight = [Math]::Max(240, [Math]::Floor($screenHeight / $rows))
foreach ($entry in $layout) {
    $hWnd = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        $hWnd = [Win32WindowTools]::FindWindowByTitle($entry.title)
        if ($hWnd -ne [IntPtr]::Zero) {
            break
        }

        Start-Sleep -Milliseconds 100
    }

    if ($hWnd -eq [IntPtr]::Zero) {
        Write-Warning ("Failed to find window for {0}: {1}" -f $entry.serverId, $entry.title)
        continue
    }

    $row = [Math]::Floor($entry.index / $columns)
    $column = $entry.index % $columns
    [Win32WindowTools]::MoveWindow(
        $hWnd,
        [int]($column * $cellWidth),
        [int]($row * $cellHeight),
        [int]$cellWidth,
        [int]$cellHeight,
        $true
    ) | Out-Null
}
"""
    env = dict(**os.environ)
    env["DEGF_WINDOW_LAYOUT"] = json.dumps(
        [
            {"serverId": server_id, "title": make_window_title(server_id), "index": index}
            for index, server_id in enumerate(server_ids)
        ]
    )
    env["DEGF_WINDOW_ROWS"] = str(WINDOW_LAYOUT_ROWS)
    env["DEGF_WINDOW_COLUMNS"] = str(WINDOW_LAYOUT_COLUMNS)
    subprocess.Popen(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
        env=env,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )


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
            title = make_window_title(server_id)
            popen_kwargs: dict[str, object] = {"cwd": str(ENGINE_DIR)}

            if sys.platform == "win32":
                command = f'title {title} & "{exe_path}" "{config_path}" "{server_id}"'
                wt_path = shutil.which("wt.exe")
                if wt_path:
                    popen_kwargs["args"] = [
                        wt_path,
                        "--window",
                        "new",
                        "new-tab",
                        "--title",
                        title,
                        "--startingDirectory",
                        str(ENGINE_DIR),
                        "cmd.exe",
                        "/k",
                        command,
                    ]
                else:
                    popen_kwargs["args"] = ["cmd.exe", "/k", command]
                    popen_kwargs["creationflags"] = subprocess.CREATE_NEW_CONSOLE
            else:
                popen_kwargs["args"] = [str(exe_path), str(config_path), server_id]
                popen_kwargs["stdin"] = subprocess.DEVNULL
                popen_kwargs["stdout"] = subprocess.DEVNULL
                popen_kwargs["stderr"] = subprocess.DEVNULL

            process = subprocess.Popen(
                **popen_kwargs,
            )
            started_processes.append(process)
            print(f"Started {server_id} in window {title}")
            time.sleep(0.2)

        arrange_windows_with_powershell(server_ids)
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
