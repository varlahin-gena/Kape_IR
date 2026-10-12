#Requires -Version 5.1
<#
.SYNOPSIS
  Run one Volatility 3 plugin against memory.raw or memory.dmp (offline triage).

.NOTES
  Used by Modules\Apps\Local\Phase1\Volatility3\*.mkape
  Place standalone vol.exe from volatility3-win-exes-*.zip into Modules\bin\volatility3\

  Do NOT set ExportFile in the .mkape to the same OutputFileName - KAPE would open
  that path for stdout capture and block this script's redirect (empty ~200 B stubs).

  Keep this file ASCII-only (or UTF-8 with BOM). Windows PowerShell 5.1 defaults to
  system ANSI when there is no BOM; Unicode dashes break string literals.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string] $DestinationDirectory,

    [Parameter(Mandatory = $true)]
    [string] $VolExe,

    [Parameter(Mandatory = $true)]
    [string] $Plugin,

    [Parameter(Mandatory = $true)]
    [string] $OutputFileName,

    [ValidateSet("csv", "pretty", "json", "quick")]
    [string] $Renderer = "csv"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $VolExe)) {
    throw "vol.exe not found: $VolExe. Unpack volatility3-win-exes-*.zip into Modules\bin\volatility3\"
}

if (-not (Test-Path -LiteralPath $SourceDirectory)) {
    throw "Source directory missing: $SourceDirectory"
}

New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null

$preferredNames = @("memory.raw", "memory.dmp")
$dump = $null
foreach ($name in $preferredNames) {
    $candidate = Join-Path $SourceDirectory $name
    if (Test-Path -LiteralPath $candidate) {
        $dump = (Resolve-Path -LiteralPath $candidate).Path
        break
    }
}

if (-not $dump) {
    $dump = Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $preferredNames -contains $_.Name } |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $dump) {
    throw "No memory.raw or memory.dmp under: $SourceDirectory"
}

$outPath = Join-Path $DestinationDirectory $OutputFileName
$errPath = Join-Path $DestinationDirectory ($OutputFileName + ".stderr.txt")
$argList = @("-f", $dump, "-r", $Renderer, $Plugin)

Write-Host "Volatility3: $Plugin <- $dump -> $outPath"

# Start-Process redirects avoid PowerShell *> / Out-File fighting KAPE handles.
$proc = Start-Process -FilePath $VolExe `
    -ArgumentList $argList `
    -WorkingDirectory (Split-Path -Parent $VolExe) `
    -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput $outPath `
    -RedirectStandardError $errPath

$code = $proc.ExitCode
if ($null -eq $code) { $code = -1 }

$outLen = 0
if (Test-Path -LiteralPath $outPath) {
    $outLen = (Get-Item -LiteralPath $outPath).Length
}

if ($code -ne 0) {
    if (Test-Path -LiteralPath $errPath) {
        Get-Content -LiteralPath $errPath -ErrorAction SilentlyContinue | Select-Object -First 40 | ForEach-Object { Write-Host $_ }
    }
    Write-Warning "vol.exe exit code $code (plugin=$Plugin, output=$outPath, bytes=$outLen)"
    exit $code
}

# Real plugin CSV/text is much larger; ~200 B is usually just a banner/stub.
if ($outLen -lt 64) {
    Write-Warning "vol.exe produced tiny output ($outLen bytes) for $Plugin - treating as failure."
    exit 1
}

if (Test-Path -LiteralPath $errPath) {
    # Keep stderr only when non-empty (progress/warnings); drop noise otherwise.
    if ((Get-Item -LiteralPath $errPath).Length -eq 0) {
        Remove-Item -LiteralPath $errPath -Force -ErrorAction SilentlyContinue
    }
}
