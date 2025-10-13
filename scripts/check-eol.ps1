Param(
    [string]$Root = ".",
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'

function Write-VerboseLine {
    param([string]$Message)
    if ($VerboseOutput) { Write-Host $Message }
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

# File extensions we treat as text and validate
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

$rootPath = Resolve-Path -LiteralPath $Root
Write-VerboseLine "Scanning for EOL/BOM issues under: $rootPath"

$bomFiles = New-Object System.Collections.Generic.List[string]
$badEolFiles = New-Object System.Collections.Generic.List[string]

Get-ChildItem -LiteralPath $rootPath -Recurse -File -Force |
    ForEach-Object {
        $path = $_.FullName
        # Skip excluded directories
        foreach ($pat in $excludePatterns) { if ($path -match $pat) { return } }

        $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
        if ($extensions -notcontains $ext) { return }

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

        # Check for line endings
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        $lfOnly = [regex]::Matches($text, '(?<!\r)\n').Count
        $crOnly = [regex]::Matches($text, '\r(?!\n)').Count

        if ($lfOnly -gt 0 -or $crOnly -gt 0) {
            $badEolFiles.Add($path)
        }
    }

if ($bomFiles.Count -eq 0 -and $badEolFiles.Count -eq 0) {
    Write-Host "EOL check passed: CRLF only and no BOMs detected."
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

Write-Error "EOL/BOM policy violations detected. See lists above."
exit 1

