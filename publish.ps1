# Build PackRunner stub, then self-contained single-file Pack Builder EXE
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$tools = Join-Path $PSScriptRoot "tools"
$runnerOut = Join-Path $PSScriptRoot "artifacts\runner"
$out = Join-Path $PSScriptRoot "dist"
New-Item -ItemType Directory -Force -Path $tools, $runnerOut, $out | Out-Null

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

Write-Host ""
Write-Host "[+] Published: $out\KapePackBuilder.exe"
Get-Item "$out\KapePackBuilder.exe", "$out\KapePackRunner.exe" | Select-Object FullName, Length, LastWriteTime
