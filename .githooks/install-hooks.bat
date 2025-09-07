@echo off
REM Install git hooks for Brainarr project (Windows)

setlocal enabledelayedexpansion

REM Get the directory of this script
set HOOKS_DIR=%~dp0

REM Check if we're in a git repository
git rev-parse --git-dir >nul 2>&1
if errorlevel 1 (
    echo Error: Not in a git repository
    exit /b 1
)

REM Get the git directory
for /f "delims=" %%i in ('git rev-parse --git-dir') do set GIT_DIR=%%i

echo Installing git hooks...

REM Create hooks directory if it doesn't exist
if not exist "%GIT_DIR%\hooks" mkdir "%GIT_DIR%\hooks"

REM Install pre-commit hook
if exist "%HOOKS_DIR%pre-commit" (
    copy /Y "%HOOKS_DIR%pre-commit" "%GIT_DIR%\hooks\pre-commit" >nul
    echo Pre-commit hook installed
) else (
    echo Warning: Pre-commit hook not found in %HOOKS_DIR%
)

echo.
echo Git hooks installation complete!
echo.
echo To bypass hooks temporarily, use: git commit --no-verify
