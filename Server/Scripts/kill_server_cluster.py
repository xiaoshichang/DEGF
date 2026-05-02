from __future__ import annotations

import json
import os
import subprocess
import sys
import time

PROCESS_KEYWORD = "DEServer"
WINDOW_TITLE_PREFIX = "DEGF-"


def close_cluster_windows() -> None:
    if sys.platform != "win32":
        return

    powershell = r"""
Add-Type @"
using System;
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
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public static string GetTitle(IntPtr hWnd) {
        int length = GetWindowTextLength(hWnd);
        if (length <= 0) {
            return "";
        }

        StringBuilder builder = new StringBuilder(length + 1);
        GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }
}
"@
$prefix = $env:DEGF_WINDOW_TITLE_PREFIX
$script:closed = 0
[Win32WindowTools]::EnumWindows({
    param([IntPtr]$hWnd, [IntPtr]$lParam)
    if (-not [Win32WindowTools]::IsWindowVisible($hWnd)) {
        return $true
    }

    $title = [Win32WindowTools]::GetTitle($hWnd)
    if ($title.Contains($prefix)) {
        [Win32WindowTools]::PostMessage($hWnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        $script:closed += 1
    }

    return $true
}, [IntPtr]::Zero) | Out-Null
Write-Output $script:closed
"""
    completed = subprocess.run(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", powershell],
        env={**os.environ, "DEGF_WINDOW_TITLE_PREFIX": WINDOW_TITLE_PREFIX},
        capture_output=True,
        text=True,
        check=False,
    )
    if completed.returncode != 0:
        combined_output = f"{completed.stdout}\n{completed.stderr}".strip()
        raise RuntimeError(f"Failed to close cluster windows: {combined_output}")

    output = completed.stdout.strip()
    if output:
        print(f"Closed {output} cluster window(s)")


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
        close_cluster_windows()
        return

    for entry in processes:
        pid = int(entry["pid"])
        print(f'Stopping {entry["name"]} (PID {pid})')
        kill_pid(pid)

    time.sleep(0.2)
    close_cluster_windows()


def main() -> int:
    try:
        kill_cluster()
        return 0
    except Exception as exc:
        print(f"Failed to kill cluster: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
