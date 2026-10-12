# Двухфазный IR / Phase1

Режим **two_phase** в Builder: на целевом хосте сначала оперативный сбор, затем диск. First-party compounds и leaf’ы засеваются в корень KAPE как каталог **KapeIR** (см. раздел Builder).

## Порядок фаз

1. **1a — collect** → `RESULTS\<host>\Phase1_Volatile\`  
2. **1b — Vol3** (по умолчанию вкл.) → `Phase1_Volatile\MemoryAnalysis\` и `MemoryDeep\`  
3. **2 — disk** → `Phase2_Disk\` (+ ModuleOutput по пакету)

На целевой машине 1b можно отключить без отказа от дампа (`--skip-memory-analysis` / чекбокс в Triage). Подробнее флаги — в разделе «Triage и режимы».

## Compounds (KapeIR)

| Compound | Роль |
|----------|------|
| `Phase1_VolatileFirst` | Фаза 1a: WinPmem → сеть → процессы → net/system info → system snapshot |
| `Phase1_VolatileFirst_NoMemory` | То же без RAM (`--skip-memory`) |
| `Phase1_MemoryTriage` | Фаза 1b triage → `MemoryAnalysis\` |
| `Phase1_MemoryDeep` | Фаза 1b deep → `MemoryDeep\` |
| `Hayabusa_Offline_v41` | Обычно **фаза 2** (offline EVTX hunt), не часть 1a/1b |

Leaf-модули Phase1 / Volatility3 / Hayabusa v41 лежат под `Modules\Apps\Local\…` и подтягиваются как зависимости compounds; в пакет их перечислять вручную не обязательно.

## Бинарники в `Modules\bin`

| Нужно для | Путь |
|-----------|------|
| Дамп 1a | `winpmem.exe` |
| Live helpers 1a | `Invoke-Utf8Capture.ps1`, `Get-*.ps1`, Sysinternals (handle/pslist/…) |
| Пост-анализ 1b | `volatility3\vol.exe` (+ скрипт плагинов) |

Установка: Builder → **Обновить с GitHub…** (блок Phase1 / KapeIR). Без `vol.exe` 1b пропускается с предупреждением в логе; без `winpmem.exe` полный 1a с дампом не стартует (нужен `--skip-memory` или доустановка).

## В пакете Builder

Для two_phase в экспорт входят compounds 1a (+ `_NoMemory`), `Phase1_MemoryTriage`, `Phase1_MemoryDeep` и их зависимости. Включение 1b **в EXE** не означает, что Vol3 обязан бежать на каждом хосте — это решает оператор при запуске Triage.
