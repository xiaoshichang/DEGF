@echo off
setlocal

set "SCRIPT_DIR=%~dp0"

where py >nul 2>nul
if not errorlevel 1 (
    py -3 "%SCRIPT_DIR%start_server_cluster.py" %*
    exit /b %errorlevel%
)

python "%SCRIPT_DIR%start_server_cluster.py" %*
exit /b %errorlevel%
