# KAPE Pack Builder

Desktop GUI (C# / WPF) for constructing and refining full KAPE triage packages —
compound targets + modules + launch scripts + portable ZIP.

## Requirements
- Windows x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build from source

## Build single EXE
```powershell
.\publish.ps1
```
Output: `dist\KapePackBuilder.exe` (self-contained, one file).

## Run
```
dist\KapePackBuilder.exe
```
Default KAPE root: last used path, parent of the EXE, or browse in the UI header.

## Features
- Fast virtualized Targets / Modules lists with search
- Compound tree with shared-pack badges and multi-pack merge
- Load existing compounds / `package.json`
- Export full package (install into KAPE, ZIP, copy deps)
- Update Targets/Modules from [EricZimmerman/KapeFiles](https://github.com/EricZimmerman/KapeFiles)

## Notes
- Local-only custom targets are kept across GitHub sync
- `Modules\bin` is not overwritten by sync and is not auto-copied into ZIP exports
