#!/usr/bin/env bash
set -euo pipefail
command -v pwsh >/dev/null 2>&1 || { echo "pwsh required. Install: https://aka.ms/powershell"; exit 1; }
exec pwsh -NoProfile -ExecutionPolicy Bypass -File "$(dirname "$0")/verify-local.ps1" "$@"
