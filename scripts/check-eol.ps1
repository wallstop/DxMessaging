Param(
    [string]$Root = ".",
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'

function Write-VerboseLine {
    param([string]$Message)
    if ($VerboseOutput) { Write-Host $Message }
}

function Get-GitIndexEolIssues {
    param([string[]]$Extensions)

    $issues = New-Object System.Collections.Generic.List[string]
    try {
        $gitRoot = git rev-parse --show-toplevel 2>$null
    }
    catch {
        return $issues
    }

    if (-not $gitRoot) {
        return $issues
    }

    $gitRoot = $gitRoot.Trim()
    try {
        $lines = git -C $gitRoot ls-files --eol 2>$null
    }
    catch {
        return $issues
    }

    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split "`t", 2
        if ($parts.Count -lt 2) { continue }

        $meta = $parts[0].Trim()
        $relPath = $parts[1].Trim()
        $ext = [System.IO.Path]::GetExtension($relPath).ToLowerInvariant()
        if ($Extensions -notcontains $ext) { continue }

        # git ls-files --eol output format: i/[eol] w/[eol] attr/[attrs] [path]
        $tokens = $meta -split '\s+'
        $attrToken = $tokens | Where-Object { $_ -like 'attr/*' } | Select-Object -First 1
        if ($attrToken -eq 'attr/-text') { continue }
        
        $indexToken = $tokens | Where-Object { $_ -like 'i/*' } | Select-Object -First 1
        if (-not $indexToken) { continue }
        if ($indexToken -ne 'i/lf' -and $indexToken -ne 'i/none') {
            $issues.Add("$relPath ($indexToken)")
        }
    }

    return $issues
}

# Directories to exclude from scanning
$excludePatterns = @(
    "[\\/]\.git[\\/]",
    "[\\/]node_modules[\\/]",
    "[\\/]Library[\\/]",
    "[\\/]Obj[\\/]|[\\/]obj[\\/]",
    "[\\/]Temp[\\/]",
    "[\\/]Samples~[\\/]",
    "[\\/]\.vs[\\/]"
)

# File extensions we treat as text and validate (CRLF expected)
$extensions = @(
    '.cs', '.csproj', '.sln',
    '.json', '.jsonc', '.toml',
    '.yaml', '.yml',
    '.md', '.markdown',
    '.xml', '.uxml', '.uss',
    '.shader', '.hlsl', '.compute', '.cginc',
    '.asmdef', '.asmref', '.meta',
    '.ps1'
)

# Shell scripts must use LF for Unix compatibility
$lfExtensions = @(
    '.sh'
)

# All extensions to scan (CRLF + LF types)
$allExtensions = $extensions + $lfExtensions

$rootPath = Resolve-Path -LiteralPath $Root
Write-VerboseLine "Scanning for EOL/BOM issues under: $rootPath"

$bomFiles = New-Object System.Collections.Generic.List[string]
$badEolFiles = New-Object System.Collections.Generic.List[string]
$indexIssues = Get-GitIndexEolIssues -Extensions $allExtensions

Get-ChildItem -LiteralPath $rootPath -Recurse -File -Force |
    ForEach-Object {
        $path = $_.FullName
        # Skip excluded directories
        foreach ($pat in $excludePatterns) { if ($path -match $pat) { return } }

        $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
        if ($allExtensions -notcontains $ext) { return }

        try {
            $bytes = [System.IO.File]::ReadAllBytes($path)
        }
        catch {
            Write-VerboseLine "Skipping unreadable file: $path"
            return
        }

        # Check for UTF-8 BOM (EF BB BF)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            $bomFiles.Add($path)
        }

        # Check for line endings based on file type
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        
        if ($lfExtensions -contains $ext) {
            # Shell scripts should have LF only (no CRLF or bare CR)
            $crlfCount = [regex]::Matches($text, '\r\n').Count
            $crOnly = [regex]::Matches($text, '\r(?!\n)').Count
            if ($crlfCount -gt 0 -or $crOnly -gt 0) {
                $badEolFiles.Add($path)
            }
        }
        else {
            # All other text files should have CRLF (no bare LF or bare CR)
            $lfOnly = [regex]::Matches($text, '(?<!\r)\n').Count
            $crOnly = [regex]::Matches($text, '\r(?!\n)').Count
            if ($lfOnly -gt 0 -or $crOnly -gt 0) {
                $badEolFiles.Add($path)
            }
        }
    }

if ($bomFiles.Count -eq 0 -and $badEolFiles.Count -eq 0 -and $indexIssues.Count -eq 0) {
    Write-Host "EOL check passed: line endings correct and no BOMs detected."
    exit 0
}

if ($bomFiles.Count -gt 0) {
    Write-Host "Files contain a UTF-8 BOM (should be no BOM):"
    $bomFiles | ForEach-Object { Write-Host "  $_" }
}

if ($badEolFiles.Count -gt 0) {
    Write-Host "Files contain non-CRLF line endings (found LF or bare CR):"
    $badEolFiles | ForEach-Object { Write-Host "  $_" }
}

if ($indexIssues.Count -gt 0) {
    Write-Host "Git index contains non-normalized line endings (expected LF in repo for text files):"
    $indexIssues | ForEach-Object { Write-Host "  $_" }
    Write-Host "Fix: git add --renormalize ."
}

Write-Error "EOL/BOM policy violations detected. See lists above."
exit 1

