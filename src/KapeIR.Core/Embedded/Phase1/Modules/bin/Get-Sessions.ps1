#Requires -Version 5.1
<#
.SYNOPSIS
  Session list for KAPE LiveResponse (works on Windows Home without query.exe).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

$ErrorActionPreference = 'SilentlyContinue'
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add(('CollectedUtc={0}' -f (Get-Date).ToUniversalTime().ToString('o')))
$lines.Add("ComputerName=$env:COMPUTERNAME")

$query = $null
foreach ($cand in @(
        (Join-Path $env:SystemRoot 'System32\query.exe'),
        (Join-Path $env:SystemRoot 'Sysnative\query.exe'),
        'query.exe'
    )) {
    if ($cand -eq 'query.exe') {
        $cmd = Get-Command query.exe -ErrorAction SilentlyContinue
        if ($cmd) { $query = $cmd.Source; break }
    }
    elseif (Test-Path -LiteralPath $cand) {
        $query = $cand
        break
    }
}

if ($query) {
    $lines.Add("Source=query session ($query)")
    $lines.Add((& $query session 2>&1 | Out-String).TrimEnd())
}
else {
    $lines.Add('Source=PowerShellFallback (query.exe not present - typical on Windows Home)')
}

# qwinsta is often the same binary family; try if present
$qwinsta = Join-Path $env:SystemRoot 'System32\qwinsta.exe'
if (Test-Path -LiteralPath $qwinsta) {
    $lines.Add('--- qwinsta ---')
    $lines.Add((& $qwinsta 2>&1 | Out-String).TrimEnd())
}

Get-Process -IncludeUserName -ErrorAction SilentlyContinue |
    Where-Object { $_.SessionId -ge 0 -and $_.UserName } |
    Group-Object SessionId |
    ForEach-Object {
        $user = ($_.Group | Select-Object -ExpandProperty UserName -Unique) -join ','
        $procs = ($_.Group | Select-Object -ExpandProperty Name -Unique | Select-Object -First 8) -join ','
        $lines.Add("SessionId=$($_.Name) Users=$user SampleProcs=$procs")
    }

$dir = Split-Path -Parent $OutputFile
if ($dir -and -not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
$lines -join "`r`n" | Out-File -LiteralPath $OutputFile -Encoding utf8
