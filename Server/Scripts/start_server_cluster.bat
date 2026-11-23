@echo off
setlocal

set "SCRIPT_DIR=%~dp0"

py -3 --version >nul 2>nul
if not errorlevel 1 goto run_with_py

python "%SCRIPT_DIR%start_server_cluster.py" %*
exit /b %errorlevel%

:run_with_py
py -3 "%SCRIPT_DIR%start_server_cluster.py" %*
exit /b %errorlevel%
