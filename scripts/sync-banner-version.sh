#!/bin/bash
#
# Wrapper script to run sync-banner-version.ps1 with PowerShell.
# This script is used by pre-commit hooks to keep YAML lines short.
#
# If PowerShell is not available, the script exits successfully with a warning
# because the banner version is purely cosmetic.
#

if command -v pwsh >/dev/null 2>&1; then
    pwsh -NoProfile -File scripts/sync-banner-version.ps1
elif command -v powershell >/dev/null 2>&1; then
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/sync-banner-version.ps1
else
    echo "PowerShell not found; skipping banner sync (cosmetic only)"
fi
