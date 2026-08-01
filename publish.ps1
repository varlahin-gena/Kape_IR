# Build self-contained single-file EXE
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$out = Join-Path $PSScriptRoot "dist"
New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet publish .\src\KapePackBuilder\KapePackBuilder.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o $out

Write-Host ""
Write-Host "[+] Published: $out\KapePackBuilder.exe"
Get-Item "$out\KapePackBuilder.exe" | Select-Object FullName, Length, LastWriteTime
