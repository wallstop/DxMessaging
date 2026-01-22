<#
.SYNOPSIS
    Generate or check the LLM skills index.

.DESCRIPTION
    This script delegates to scripts/generate-skills-index.js to keep output
    consistent across platforms and tooling.

.EXAMPLE
    pwsh scripts/generate-skills-index.ps1

.EXAMPLE
    pwsh scripts/generate-skills-index.ps1 -Check
#>

[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$JsScript = Join-Path $ScriptDir 'generate-skills-index.js'

$node = Get-Command node -ErrorAction SilentlyContinue
if ($null -eq $node) {
    Write-Error "node is required to generate the skills index. Install Node.js and run $JsScript."
    exit 1
}

$args = @()
if ($Check) {
    $args += '--check'
}

& $node.Source $JsScript @args
exit $LASTEXITCODE
