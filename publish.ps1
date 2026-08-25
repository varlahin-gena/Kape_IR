# Build PackRunner stub, then self-contained single-file Pack Builder EXE
# Optional Authenticode: set env KAPEPACK_SIGN_CERT to a .pfx path and KAPEPACK_SIGN_PASSWORD
param(
    [string]$SignCert = $env:KAPEPACK_SIGN_CERT,
    [string]$SignPassword = $env:KAPEPACK_SIGN_PASSWORD,
    [string]$TimestampUrl = $(if ($env:KAPEPACK_SIGN_TIMESTAMP) { $env:KAPEPACK_SIGN_TIMESTAMP } else { "http://timestamp.digicert.com" })
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$tools = Join-Path $PSScriptRoot "tools"
$runnerOut = Join-Path $PSScriptRoot "artifacts\runner"
$out = Join-Path $PSScriptRoot "dist"
New-Item -ItemType Directory -Force -Path $tools, $runnerOut, $out | Out-Null

function Invoke-OptionalSign([string]$ExePath) {
    if ([string]::IsNullOrWhiteSpace($SignCert)) {
        Write-Host "[i] Signing skipped (set KAPEPACK_SIGN_CERT to enable Authenticode)."
        return
    }
    if (-not (Test-Path $SignCert)) {
        Write-Warning "Sign cert not found: $SignCert — skipping."
        return
    }
    $signtool = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe",
        "${env:ProgramFiles}\Windows Kits\10\bin\*\x64\signtool.exe"
    ) | ForEach-Object { Get-Item $_ -ErrorAction SilentlyContinue } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName

    if (-not $signtool) {
        Write-Warning "signtool.exe not found — skipping Authenticode."
        return
    }

    Write-Host "[*] Signing $ExePath ..."
    $signArgs = @(
        "sign", "/fd", "SHA256", "/td", "SHA256", "/tr", $TimestampUrl,
        "/f", $SignCert
    )
    if (-not [string]::IsNullOrEmpty($SignPassword)) {
        $signArgs += @("/p", $SignPassword)
    }
    $signArgs += $ExePath
    & $signtool @signArgs
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for $ExePath (exit $LASTEXITCODE)" }
    Write-Host "[+] Signed: $ExePath"
}

Write-Host "[*] Publishing KapePackRunner stub (GUI)..."
dotnet publish .\src\KapePackRunner\KapePackRunner.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o $runnerOut

Copy-Item -Force (Join-Path $runnerOut "KapePackRunner.exe") (Join-Path $tools "KapePackRunner.exe")
Write-Host "[+] Stub: $tools\KapePackRunner.exe"

Write-Host "[*] Publishing KapePackBuilder..."
dotnet publish .\src\KapePackBuilder\KapePackBuilder.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o $out

# Ship stub next to PackBuilder for ResolveStubPath()
Copy-Item -Force (Join-Path $tools "KapePackRunner.exe") (Join-Path $out "KapePackRunner.exe")

Invoke-OptionalSign (Join-Path $out "KapePackBuilder.exe")
Invoke-OptionalSign (Join-Path $out "KapePackRunner.exe")

Write-Host ""
Write-Host "[+] Published: $out\KapePackBuilder.exe"
Get-Item "$out\KapePackBuilder.exe", "$out\KapePackRunner.exe" | Select-Object FullName, Length, LastWriteTime
