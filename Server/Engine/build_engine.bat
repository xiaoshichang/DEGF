@echo off
setlocal

for %%I in ("%~dp0.") do set "SCRIPT_DIR=%%~fI"
set "BUILD_ROOT=%SCRIPT_DIR%\build"
set "BUILD_DIR=%BUILD_ROOT%\engine"
set "CONFIG=%~1"

if "%CONFIG%"=="" (
    set "CONFIG=Debug"
)

set "CMAKE_EXE=cmake"
where cmake >nul 2>nul
if errorlevel 1 (
    set "CMAKE_EXE=C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
)

if not exist "%CMAKE_EXE%" (
    echo CMake not found. Please install CMake or add it to PATH.
    exit /b 1
)

echo Configuring Engine with %CONFIG%...
"%CMAKE_EXE%" -S "%SCRIPT_DIR%" -B "%BUILD_DIR%" -DBUILD_TESTING=OFF
if errorlevel 1 (
    echo Configure failed.
    exit /b 1
)

echo Building Engine with %CONFIG%...
"%CMAKE_EXE%" --build "%BUILD_DIR%" --config %CONFIG%
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo Build completed: "%BUILD_DIR%"
exit /b 0
