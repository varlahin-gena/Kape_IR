#Requires -Version 5.1
<#
.SYNOPSIS
  Run a console EXE and save stdout/stderr as UTF-8 (BOM) for KAPE LiveResponse.

.DESCRIPTION
  Console tools on Russian Windows may emit OEM CP866, UTF-8, or UTF-16.
  Captures raw stdout/stderr bytes, picks the best decode, writes UTF-8 BOM.
  Prefer valid UTF-8 (netsh and some modern tools). Otherwise score CP866/1251/437.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Exe,

    [string]$Arguments = '',

    [Parameter(Mandatory = $true)]
    [string]$OutputFile,

    # Named DoAppend so bare "-a" tool args are not ambiguous with -Append.
    [switch]$DoAppend
)

$ErrorActionPreference = 'Continue'

if (-not [System.IO.Path]::IsPathRooted($Exe)) {
    foreach ($dir in @(
            (Join-Path $env:SystemRoot 'System32'),
            (Join-Path $env:SystemRoot 'Sysnative'),
            (Join-Path $env:SystemRoot 'SysWOW64')
        )) {
        $cand = Join-Path $dir $Exe
        if (Test-Path -LiteralPath $cand) {
            $Exe = $cand
            break
        }
    }
}

if (-not (Test-Path -LiteralPath $Exe)) {
    throw "Executable not found: $Exe"
}

$outDir = Split-Path -Parent $OutputFile
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

function Get-CyrillicCount([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return 0 }
    return ([regex]::Matches($s, '[\u0400-\u04FF]')).Count
}

function Get-MojibakeScore([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return 0 }
    return ([regex]::Matches($s, '[\u2550-\u256F]')).Count
}

function Test-Utf8As1251Mojibake([string]$s) {
    # UTF-8 Cyrillic misread as CP1251: dense U+0420/U+0421 + more Cyrillic
    if ([string]::IsNullOrEmpty($s)) { return 0 }
    $pat = "[\u0420\u0421][\u0400-\u04FF]"
    return ([regex]::Matches($s, $pat)).Count
}

function ConvertFrom-ConsoleBytes([byte[]]$bytes) {
    if ($null -eq $bytes -or $bytes.Length -eq 0) { return '' }

    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        return [System.Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
    }
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
        return [System.Text.Encoding]::BigEndianUnicode.GetString($bytes, 2, $bytes.Length - 2)
    }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        return [System.Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
    }

    # Prefer valid UTF-8: modern tools (netsh) emit UTF-8; decoding those bytes
    # as CP1251 still yields "Cyrillic" and would win a naive letter-count race.
    try {
        $utf8Strict = New-Object System.Text.UTF8Encoding $false, $true
        return $utf8Strict.GetString($bytes)
    }
    catch { }

    $candidates = New-Object System.Collections.Generic.List[object]
    foreach ($cp in @(866, 1251, 437)) {
        try {
            $t = [System.Text.Encoding]::GetEncoding($cp).GetString($bytes)
            $candidates.Add([pscustomobject]@{
                    Text  = $t
                    Name  = "cp$cp"
                    Score = (Get-CyrillicCount $t) * 2 `
                        - (Get-MojibakeScore $t) * 3 `
                        - (Test-Utf8As1251Mojibake $t) * 8
                })
        }
        catch { }
    }

    if ($candidates.Count -eq 0) {
        return [System.Text.Encoding]::UTF8.GetString($bytes)
    }

    $best = $candidates | Sort-Object -Property Score -Descending | Select-Object -First 1
    return $best.Text
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Exe
$psi.Arguments = $Arguments
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true

$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi
[void]$proc.Start()

$msOut = New-Object System.IO.MemoryStream
$msErr = New-Object System.IO.MemoryStream
$outTask = $proc.StandardOutput.BaseStream.CopyToAsync($msOut)
$errTask = $proc.StandardError.BaseStream.CopyToAsync($msErr)
$proc.WaitForExit()
$outTask.Wait()
$errTask.Wait()

$outBytes = $msOut.ToArray()
$errBytes = $msErr.ToArray()
$msOut.Dispose()
$msErr.Dispose()

$text = ConvertFrom-ConsoleBytes $outBytes
if ($errBytes.Length -gt 0) {
    $errText = ConvertFrom-ConsoleBytes $errBytes
    if (-not [string]::IsNullOrEmpty($errText)) {
        if (-not [string]::IsNullOrEmpty($text) -and -not $text.EndsWith("`n")) {
            $text += "`r`n"
        }
        $text += $errText
    }
}

$utf8Bom = New-Object System.Text.UTF8Encoding $true
if ($DoAppend -and (Test-Path -LiteralPath $OutputFile)) {
    [System.IO.File]::AppendAllText($OutputFile, $text, $utf8Bom)
}
else {
    [System.IO.File]::WriteAllText($OutputFile, $text, $utf8Bom)
}

Write-Output ("OK exit={0} bytes={1} -> {2}" -f $proc.ExitCode, $outBytes.Length, $OutputFile)
exit 0
