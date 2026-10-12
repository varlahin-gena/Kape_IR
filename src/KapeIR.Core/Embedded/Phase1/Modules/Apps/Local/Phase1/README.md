# Phase 1 — KapeIR modules

## 1a — live collect

| Compound | Role |
|----------|------|
| `Phase1_VolatileFirst` | WinPmem → network → processes → net/system info → system snapshot |
| `Phase1_VolatileFirst_NoMemory` | Same without RAM (`--skip-memory`) |

Leaf helpers live here (`Windows_*Live`, `LiveResponse_SystemSnapshot`). Stock compounds stay under `Modules\Compound` / GitHub (`Velocidex_WinPmem`, `LiveResponse_NetworkDetails`, …).

Requires `Modules\bin\winpmem.exe` and `Invoke-Utf8Capture.ps1`.

## 1b — memory post (auto in Triage)

| Compound | Output folder under `Phase1_Volatile` |
|----------|----------------------------------------|
| `Phase1_MemoryTriage` | `MemoryAnalysis\` |
| `Phase1_MemoryDeep` | `MemoryDeep\` |

Leaf Volatility3 plugins: `Volatility3\`. Binary: `Modules\bin\volatility3\vol.exe` + `Invoke-Volatility3Plugin.ps1`.

Skipped when `--skip-memory`, no dump, or no `vol.exe`. Failures do not stop Phase 2.
