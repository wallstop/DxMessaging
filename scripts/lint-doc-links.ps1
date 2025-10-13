Param(
    [string]$Root = ".",
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'

# Resolve path to the actual linter script under .github/scripts
$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')
$innerScript = Join-Path $repoRoot ".github/scripts/check-markdown-links.ps1"

if (-not (Test-Path -LiteralPath $innerScript)) {
    Write-Error "Unable to locate markdown link linter at '$innerScript'."
    exit 1
}

# Invoke underlying script, forwarding Root and optional verbosity
if ($VerboseOutput) {
    & $innerScript -Root $Root -Verbose
} else {
    & $innerScript -Root $Root
}

exit $LASTEXITCODE

