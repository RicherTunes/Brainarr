@echo off
setlocal enabledelayedexpansion
REM Lidarr Duplicate Cleanup - Windows Batch Wrapper
REM Usage: cleanup-duplicates.bat [--dry-run] [--verbose]

echo 🎵 Lidarr Duplicate Artist Cleanup
echo ================================

REM Check if Python is installed
python --version >nul 2>&1
if errorlevel 1 (
    echo ❌ Error: Python not found. Please install Python 3.6+ from https://python.org
    echo    Make sure to check "Add Python to PATH" during installation
    pause
    exit /b 1
)

REM Check if requests library is installed
python -c "import requests" >nul 2>&1
if errorlevel 1 (
    echo ❌ Error: 'requests' library not found. Installing...
    pip install requests
    if errorlevel 1 (
        echo ❌ Failed to install requests. Please run: pip install requests
        pause
        exit /b 1
    )
)

REM Configuration - Load from .env file
if not exist "%~dp0.env" (
    echo ❌ Configuration file not found: .env
    echo.
    echo    1. Copy .env.example to .env in the scripts folder
    echo    2. Edit .env and set your LIDARR_URL and LIDARR_API_KEY
    echo    3. Get your API key from Lidarr Settings > General
    echo.
    pause
    exit /b 1
)

REM Load environment variables from .env file
for /f "usebackq tokens=1,2 delims==" %%a in ("%~dp0.env") do (
    set "line=%%a"
    if not "!line:~0,1!"=="#" if not "!line!"=="" (
        set "%%a=%%b"
    )
)

REM Check if configuration is valid
if "%LIDARR_API_KEY%"=="" (
    echo ❌ LIDARR_API_KEY not set in .env file
    pause
    exit /b 1
)
if "%LIDARR_API_KEY%"=="your-api-key-here" (
    echo ❌ Please update LIDARR_API_KEY in .env file with your actual API key
    pause
    exit /b 1
)
if "%LIDARR_URL%"=="" (
    echo ❌ LIDARR_URL not set in .env file
    pause
    exit /b 1
)

REM Build command with arguments
set PYTHON_CMD=python "%~dp0cleanup-duplicates.py" --url "%LIDARR_URL%" --api-key "%LIDARR_API_KEY%"

REM Add any command line arguments passed to this batch file
set ARGS=%*
if not "%ARGS%"=="" (
    set PYTHON_CMD=%PYTHON_CMD% %ARGS%
) else (
    REM Default to dry-run for safety
    set PYTHON_CMD=%PYTHON_CMD% --dry-run
    echo ℹ️  Running in DRY-RUN mode by default. Use --no-dry-run to actually remove duplicates.
    echo.
)

REM Execute the Python script
echo ▶️  Running: %PYTHON_CMD%
echo.
%PYTHON_CMD%

REM Show result
if errorlevel 1 (
    echo.
    echo ❌ Script completed with errors. Check lidarr-cleanup.log for details.
) else (
    echo.
    echo ✅ Script completed successfully!
)

echo.
echo 📋 Log file: %~dp0lidarr-cleanup.log
pause