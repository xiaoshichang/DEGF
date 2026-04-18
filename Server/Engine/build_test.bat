@echo off
setlocal

for %%I in ("%~dp0.") do set "SCRIPT_DIR=%%~fI"
set "BUILD_ROOT=%SCRIPT_DIR%\build"
set "BUILD_DIR=%BUILD_ROOT%\test"
set "CONFIG=%~1"

if "%CONFIG%"=="" (
    set "CONFIG=Debug"
)

set "CMAKE_EXE=cmake"
where cmake >nul 2>nul
if errorlevel 1 (
    set "CMAKE_EXE=C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
)

set "CTEST_EXE=ctest"
where ctest >nul 2>nul
if errorlevel 1 (
    set "CTEST_EXE=C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe"
)

if not exist "%CMAKE_EXE%" (
    echo CMake not found. Please install CMake or add it to PATH.
    exit /b 1
)

if not exist "%CTEST_EXE%" (
    echo CTest not found. Please install CMake or add it to PATH.
    exit /b 1
)

echo Configuring tests with %CONFIG%...
"%CMAKE_EXE%" -S "%SCRIPT_DIR%" -B "%BUILD_DIR%" -DBUILD_TESTING=ON
if errorlevel 1 (
    echo Configure failed.
    exit /b 1
)

echo Building tests with %CONFIG%...
"%CMAKE_EXE%" --build "%BUILD_DIR%" --config %CONFIG% --target engine_smoke_tests
if errorlevel 1 (
    echo Test build failed.
    exit /b 1
)

echo Running tests with %CONFIG%...
"%CTEST_EXE%" --test-dir "%BUILD_DIR%" -C %CONFIG% --output-on-failure
if errorlevel 1 (
    echo Tests failed.
    exit /b 1
)

echo Tests completed successfully: "%BUILD_DIR%"
exit /b 0
