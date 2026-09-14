# Build Runner stub (embedded), then ONE self-contained Pack Builder EXE for end users.
# Optional Authenticode: set env KAPEPACK_SIGN_CERT to a .pfx path and KAPEPACK_SIGN_PASSWORD
param(
    [string]$SignCert = $env:KAPEPACK_SIGN_CERT,
    [string]$SignPassword = $env:KAPEPACK_SIGN_PASSWORD,
    [string]$TimestampUrl = $(if ($env:KAPEPACK_SIGN_TIMESTAMP) { $env:KAPEPACK_SIGN_TIMESTAMP } else { "http://timestamp.digicert.com" }),
    [switch]$AlsoPublishRunner
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$tools = Join-Path $PSScriptRoot "tools"
$runnerOut = Join-Path $PSScriptRoot "artifacts\runner"
$out = Join-Path $PSScriptRoot "dist"
New-Item -ItemType Directory -Force -Path $tools, $runnerOut, $out | Out-Null

function Write-Sha256Sidecar([string]$ExePath) {
    if (-not (Test-Path $ExePath)) { return }
    $hash = (Get-FileHash -Path $ExePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $name = Split-Path $ExePath -Leaf
    $sidecar = "$ExePath.sha256"
    Set-Content -Path $sidecar -Value "$hash  $name" -Encoding ascii -NoNewline
    Add-Content -Path $sidecar -Value "" -Encoding ascii
    Write-Host "[+] SHA256: $sidecar"
}

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

Write-Host "[*] Publishing KapePackRunner stub (embedded into Builder)..."
dotnet publish .\src\KapePackRunner\KapePackRunner.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o $runnerOut

$stubExe = Join-Path $runnerOut "KapePackRunner.exe"
Copy-Item -Force $stubExe (Join-Path $tools "KapePackRunner.exe")
Write-Sha256Sidecar (Join-Path $tools "KapePackRunner.exe")
Write-Host "[+] Stub cached: $tools\KapePackRunner.exe"

Write-Host "[*] Publishing KapePackBuilder (single deliverable)..."
dotnet publish .\src\KapePackBuilder\KapePackBuilder.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o $out

$builderExe = Join-Path $out "KapePackBuilder.exe"
Invoke-OptionalSign $builderExe
Write-Sha256Sidecar $builderExe

# Optional separate Runner for debugging; not required next to Builder anymore.
if ($AlsoPublishRunner) {
    Copy-Item -Force (Join-Path $tools "KapePackRunner.exe") (Join-Path $out "KapePackRunner.exe")
    $runnerDist = Join-Path $out "KapePackRunner.exe"
    Invoke-OptionalSign $runnerDist
    Write-Sha256Sidecar $runnerDist
} else {
    $legacyRunner = Join-Path $out "KapePackRunner.exe"
    if (Test-Path $legacyRunner) {
        Remove-Item -Force $legacyRunner
        Write-Host "[i] Removed dist\KapePackRunner.exe (stub is embedded in Builder)."
    }
    $legacyHash = Join-Path $out "KapePackRunner.exe.sha256"
    if (Test-Path $legacyHash) { Remove-Item -Force $legacyHash }
}

Write-Host ""
Write-Host "[+] Primary deliverable (one EXE): $builderExe"
Get-Item $builderExe | Select-Object FullName, Length, LastWriteTime
if (Test-Path "$builderExe.sha256") {
    Get-Content "$builderExe.sha256"
}
if ($AlsoPublishRunner) {
    Get-Item "$out\KapePackRunner.exe" | Select-Object FullName, Length, LastWriteTime
}
