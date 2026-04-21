@echo off
setlocal

for %%I in ("%~dp0.") do set "SCRIPT_DIR=%%~fI"
set "BUILD_ROOT=%SCRIPT_DIR%\build"
set "BUILD_DIR=%BUILD_ROOT%\engine"
set "CONFIG=%~1"

if "%CONFIG%"=="" (
    set "CONFIG=Debug"
)

echo Configuring Engine with %CONFIG%...
cmake -S "%SCRIPT_DIR%" -B "%BUILD_DIR%" -DBUILD_TESTING=OFF
if errorlevel 1 (
    echo Configure failed.
    exit /b 1
)

echo Building Engine with %CONFIG%...
cmake --build "%BUILD_DIR%" --config %CONFIG%
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo Build completed: "%BUILD_DIR%"
exit /b 0
