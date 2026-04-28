@echo off
setlocal

for %%I in ("%~dp0.") do set "SCRIPT_DIR=%%~fI"
set "SOLUTION_FILE=%SCRIPT_DIR%\Framework.sln"
set "CONFIG=%~1"

if "%CONFIG%"=="" (
    set "CONFIG=Debug"
)

set "DOTNET_EXE=dotnet"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet not found. Please install .NET SDK or add it to PATH.
    pause
    exit /b 1
)

if not exist "%SOLUTION_FILE%" (
    echo Solution file not found: "%SOLUTION_FILE%"
    pause
    exit /b 1
)

echo Building Framework with %CONFIG%...
"%DOTNET_EXE%" build "%SOLUTION_FILE%" -c %CONFIG% -v minimal
if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)

echo Build completed: "%SOLUTION_FILE%"
pause
exit /b 0
