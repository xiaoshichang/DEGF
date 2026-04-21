@echo off
setlocal

for %%I in ("%~dp0.") do set "SCRIPT_DIR=%%~fI"
set "BUILD_ROOT=%SCRIPT_DIR%\build"
set "BUILD_DIR=%BUILD_ROOT%\test"
set "CONFIG=%~1"

if "%CONFIG%"=="" (
    set "CONFIG=Debug"
)

echo Configuring tests with %CONFIG%...
cmake -S "%SCRIPT_DIR%" -B "%BUILD_DIR%" -DBUILD_TESTING=ON
if errorlevel 1 (
    echo Configure failed.
    exit /b 1
)

echo Building tests with %CONFIG%...
cmake --build "%BUILD_DIR%" --config %CONFIG% --target engine_smoke_tests
if errorlevel 1 (
    echo Test build failed.
    exit /b 1
)

echo Running tests with %CONFIG%...
ctest --test-dir "%BUILD_DIR%" -C %CONFIG% --output-on-failure
if errorlevel 1 (
    echo Tests failed.
    exit /b 1
)

echo Tests completed successfully: "%BUILD_DIR%"
exit /b 0
