<#
.SYNOPSIS
    Syncs the version from package.json to the SVG banner.
.DESCRIPTION
    Reads the version from package.json and updates the version badge in the
    dxmessaging-banner.svg file. Automatically stages the SVG if modified.
    
    Called by the pre-commit hook before each commit is created.
    
    NOTE: This script is optional in the pre-commit workflow. If PowerShell
    is not available, the hook skips banner sync because:
    - The banner version is purely cosmetic (affects README appearance only)
    - Contributors without PowerShell should not be blocked from committing
    - The banner can be updated manually or via CI if needed
#>

$ErrorActionPreference = 'Stop'

# Get the script directory and navigate to repo root
$scriptDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptDir

# Construct paths for package.json and SVG
$packageJsonPath = Join-Path $repoRoot "package.json"
$svgPath = Join-Path $repoRoot "docs" "images" "dxmessaging-banner.svg"

# Check if package.json exists
if (-not (Test-Path $packageJsonPath)) {
    Write-Error "package.json not found at: $packageJsonPath"
    exit 1
}

# Check if SVG exists
if (-not (Test-Path $svgPath)) {
    Write-Error "SVG banner not found at: $svgPath"
    exit 1
}

# Read version from package.json
try {
    $packageJson = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
    $version = $packageJson.version
    if (-not $version) {
        Write-Error "No version field found in package.json"
        exit 1
    }
    # Validate semver format (X.Y.Z with optional pre-release/build metadata)
    if ($version -notmatch '^\d+\.\d+\.\d+') {
        Write-Error "Invalid version format in package.json: $version (expected semver X.Y.Z)"
        exit 1
    }
} catch {
    Write-Error "Failed to read package.json: $_"
    exit 1
}

# Read the SVG content
try {
    $svgContent = Get-Content $svgPath -Raw
} catch {
    Write-Error "Failed to read SVG file: $_"
    exit 1
}

# Pattern to find version text in the SVG
# SYNC: Keep pattern in sync with the SVG banner format
# The pattern anchors to the Version badge comment to avoid matching other version-like text
# Note: Uses .*? (non-greedy) for comment body to match anything up to -->
$versionPattern = '<!-- Version badge \(top right\).*?-->\s*<g[^>]*>\s*<rect[^>]*/>\s*<text[^>]*>v[\d]+\.[\d]+\.[\d]+[^<]*</text>'
$newVersionText = @"
<!-- Version badge (top right) - text must contain vX.Y.Z for version sync -->
  <g transform="translate(720, 25)">
    <rect x="0" y="-12" width="60" height="22" rx="11" fill="#e94560" filter="url(#softShadow)"/>
    <text x="30" y="4" text-anchor="middle" font-family="'SF Mono', 'Fira Code', monospace" font-size="12" font-weight="600" fill="#ffffff" letter-spacing="0.5">v$version</text>
"@

# Check if the pattern matches
if ($svgContent -notmatch $versionPattern) {
    Write-Error "Could not find version pattern in SVG. Banner format may have changed."
    Write-Error "Expected pattern: $versionPattern"
    exit 1
}

# Extract the current version from the match
$currentMatch = [regex]::Match($svgContent, $versionPattern).Value

# Extract just the version number from the current match for comparison
$currentVersionMatch = [regex]::Match($currentMatch, '>v([\d]+\.[\d]+\.[\d]+[^<]*)</text>')
if ($currentVersionMatch.Success) {
    $currentVersion = $currentVersionMatch.Groups[1].Value
    if ($currentVersion -eq $version) {
        Write-Host "Banner already has correct version: v$version"
        exit 0
    }
}

# Replace the version in the SVG
$newSvgContent = $svgContent -replace $versionPattern, $newVersionText

# Write the updated SVG
# Use .NET WriteAllText for UTF-8 without BOM (cross-platform compatible)
try {
    [System.IO.File]::WriteAllText($svgPath, $newSvgContent)
} catch {
    Write-Error "Failed to write SVG file: $_"
    exit 1
}

# Stage the modified SVG using git add
try {
    Push-Location $repoRoot
    git add $svgPath
    if ($LASTEXITCODE -ne 0) {
        throw "git add failed with exit code $LASTEXITCODE"
    }
} catch {
    Write-Error "Failed to stage SVG file: $_"
    exit 1
} finally {
    Pop-Location
}

Write-Host "Updated banner version to: v$version"
exit 0
