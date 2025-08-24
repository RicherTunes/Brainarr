@echo off
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

REM Configuration - EDIT THESE VALUES
set LIDARR_URL=http://localhost:8686
set LIDARR_API_KEY=YOUR_API_KEY_HERE

REM Check if user has configured the script
if "%LIDARR_API_KEY%"=="YOUR_API_KEY_HERE" (
    echo ❌ Please edit this batch file and set your LIDARR_URL and LIDARR_API_KEY
    echo.
    echo    1. Open cleanup-duplicates.bat in a text editor
    echo    2. Change LIDARR_URL to your Lidarr URL (e.g., http://localhost:8686)
    echo    3. Change LIDARR_API_KEY to your API key from Lidarr Settings > General
    echo.
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