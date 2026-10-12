#Requires -Version 5.1
<#
.SYNOPSIS
  Logged-on users for KAPE LiveResponse (works on Windows Home without quser.exe).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

$ErrorActionPreference = 'SilentlyContinue'
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add(('CollectedUtc={0}' -f (Get-Date).ToUniversalTime().ToString('o')))
$lines.Add("ComputerName=$env:COMPUTERNAME")

$quser = $null
foreach ($cand in @(
        (Join-Path $env:SystemRoot 'System32\quser.exe'),
        (Join-Path $env:SystemRoot 'Sysnative\quser.exe'),
        'quser.exe'
    )) {
    if ($cand -eq 'quser.exe') {
        $cmd = Get-Command quser.exe -ErrorAction SilentlyContinue
        if ($cmd) { $quser = $cmd.Source; break }
    }
    elseif (Test-Path -LiteralPath $cand) {
        $quser = $cand
        break
    }
}

if ($quser) {
    $lines.Add("Source=quser ($quser)")
    $lines.Add((& $quser 2>&1 | Out-String).TrimEnd())
}
else {
    $lines.Add('Source=PowerShellFallback (quser.exe not present - typical on Windows Home)')
}

$cs = Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction SilentlyContinue
if ($cs) {
    $lines.Add("ComputerSystem.UserName=$($cs.UserName)")
    $lines.Add("ComputerSystem.Domain=$($cs.Domain)")
}

Get-CimInstance -ClassName Win32_LogonSession -ErrorAction SilentlyContinue |
    Where-Object { $_.LogonType -in 2, 10, 11 } |
    ForEach-Object {
        $session = $_
        $accounts = Get-CimAssociatedInstance -InputObject $session -ResultClassName Win32_Account -ErrorAction SilentlyContinue
        if (-not $accounts) {
            $lines.Add("LogonSession LogonId=$($session.LogonId) LogonType=$($session.LogonType) StartTime=$($session.StartTime)")
            return
        }
        foreach ($a in $accounts) {
            $lines.Add("LogonSession LogonId=$($session.LogonId) LogonType=$($session.LogonType) StartTime=$($session.StartTime) Account=$($a.Caption)")
        }
    }

Get-Process -IncludeUserName -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'explorer' -and $_.UserName } |
    Select-Object -ExpandProperty UserName -Unique |
    ForEach-Object { $lines.Add("ExplorerSessionUser=$_") }

$dir = Split-Path -Parent $OutputFile
if ($dir -and -not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
$lines -join "`r`n" | Out-File -LiteralPath $OutputFile -Encoding utf8
