# KapeIR

Десктопная GUI-утилита (C# / WPF) для сборки triage-пакетов на базе [KAPE](https://www.kroll.com/en/services/cyber-risk/incident-response-litigation-support/kroll-artifact-parser-extractor-kape): выбор Targets/Modules, подсказки парсеров, обновление каталога и **автономный KapeIR.Triage EXE** для сбора на целевой системе.

Интерфейс на русском. Термины KAPE (`tsource`, `--zip`, `--sim`, compound, leaf, `.tkape` / `.mkape`) сохранены.

**Версия: 1.9.0**

> В этом репозитории **нет** полного дистрибутива KAPE (`kape.exe` + stock Targets/Modules). Нужна отдельная установка KAPE рядом с Builder.  
> **Phase 1 IR** (модули + скрипты) **вшит в KapeIR** и автоматически засевается в выбранный корень; WinPmem / Volatility3 ставятся через «Обновить с GitHub…» или при сборке two_phase.

---

## Скачать

Готовый файл — в разделе **[Releases](https://github.com/varlahin-gena/Kape_IR/releases/latest)**:

| Файл | Назначение |
|------|------------|
| **`KapeIR.exe`** | Единственный нужный файл (Builder; stub Triage встроен) |
| `KapeIR.exe.sha256` | Контрольная сумма SHA-256 |

Проверка: `Get-FileHash .\KapeIR.exe -Algorithm SHA256`

---

## Что это за система

**KapeIR** — рабочее место аналитика IR / DFIR: из полного каталога KAPE собирается полевой пакет без ручной возни с CLI и копированием деревьев.

```
┌─────────────────────┐     export      ┌──────────────────────┐
│  KapeIR             │ ──────────────► │  <ИмяПакета>.exe     │
│  (каталог, выбор,   │   один EXE      │  (KapeIR.Triage)     │
│   sync, two-phase)  │                 │  GUI или --silent    │
└─────────────────────┘                 └──────────────────────┘
          ▲                                        │
          │ указывает                              ▼
┌─────────────────────┐                 ┌──────────────────────┐
│  Локальный KAPE     │                 │  RESULTS\<host>\…    │
│  Targets / Modules  │                 │  + манисты / CoC   │
└─────────────────────┘                 └──────────────────────┘
```

| Компонент | Роль |
|-----------|------|
| **KapeIR** | Каталог Targets/Modules, фильтры, подсказки модулей, готовые сборки (CRUD), sync с GitHub KapeFiles, EZ Tools / Chainsaw |
| **KapeIR.Triage** | Портативный сборщик: распаковка рядом с EXE, запуск `kape.exe`, GUI-прогресс или silent |
| **Two-phase IR** | Phase 1a volatile + Phase 1b Vol3 (авто) → Phase 2 disk; каталог Phase1 вшит в Builder |

KAPE остаётся движком сбора; KapeIR — оболочка над каталогом и упаковкой.

---

## Требования

- Windows x64
- Установленный / распакованный **KAPE** (`Targets`, `Modules`, `kape.exe`) — путь указывается в Builder
- Права администратора на целевой системе для полного triage (UAC в KapeIR.Triage)

---

## Быстрый старт

1. Скачайте `KapeIR.exe` из Releases
2. Запустите, укажите корень KAPE
3. Отметьте Targets и Modules (при необходимости — **Подсказать по таргетам**)
4. Для IR: включите **Двухфазный IR**, задайте Case ID
5. **Собрать EXE** → на выходе `ИмяПакета.exe` (+ `.sha256`)
6. На целевой: GUI («Оценить» / «Начать сбор») или `ИмяПакета.exe --silent --tsource C:`

Выбор — из **полного** каталога Targets/Modules (Antivirus, Apps, Browsers, Compound, Logs, P2P, Windows, EZTools, custom), не из одного compound.

---

## KapeIR.Triage — режимы

Пакет **портативен**: staging и `RESULTS` рядом с EXE (флешка / шара / диск), не в `%TEMP%` на C:.

| Режим | Запуск |
|------|--------|
| GUI | `ИмяПакета.exe` → диск → «Оценить» / «Начать сбор» |
| Silent | `ИмяПакета.exe --silent --tsource C:` |
| Только оценка | `ИмяПакета.exe --sim-only --tsource C:` |
| Two-phase IR | `ИмяПакета.exe --silent --tsource C: --case-id IR-001` |
| Только volatile | `ИмяПакета.exe --silent --tsource C: --phase 1` |
| Без RAM dump | `ИмяПакета.exe --silent --tsource C: --skip-memory` |

### Двухфазный IR (Phase1)

Порядок по RFC 3227 (Order of Volatility): сначала живые данные и автопост памяти, потом диск.

#### Фаза 1a — volatile collect (`Phase1_VolatileFirst` → `RESULTS\<host>\Phase1_Volatile`)

| Шаг | Модуль | Что снимает |
|-----|--------|-------------|
| 1 | `Velocidex_WinPmem` | RAM-дамп (`memory.raw`) + `memory_hash.sha256` |
| 2 | `LiveResponse_NetworkDetails` | IP/DNS/ARP/маршрут, netstat, NetBIOS |
| 3 | `LiveResponse_ProcessDetails` | Процессы, дерево, сервисы, handles, injected threads |
| 4 | `LiveResponse_NetSystemInfo` | `net user` / groups / sessions / shares |
| 5 | `LiveResponse_SystemSnapshot` | Время, env, quser/session, schtasks, Run-keys, services |

Без RAM: `Phase1_VolatileFirst_NoMemory` или `--skip-memory`.  
Нужен signed **WinPmem** → `Modules\bin\winpmem.exe`. Без него — `--skip-memory` или exit 2.

#### Фаза 1b — автопост памяти (тот же прогон Triage)

При наличии `memory.raw`/`memory.dmp` и `Modules\bin\volatility3\vol.exe`:

| Модуль | Вывод |
|--------|--------|
| `Phase1_MemoryTriage` | `Phase1_Volatile\MemoryAnalysis\` |
| `Phase1_MemoryDeep` | `Phase1_Volatile\MemoryDeep\` |

Нет дампа / нет vol / ошибка Vol3 → warning, **фаза 2 всё равно идёт**.

#### Фаза 2 — disk (`Phase2_Disk`)

Выбранные в Builder **Targets** и парсеры (**Modules**) — по желанию пакета.

#### После сбора

`collection_log.txt`, `evidence_manifest.sha256`, `chain_of_custody.txt` (с Case ID, если задан).

---

## Возможности Builder

- Виртуализированные списки Targets / Modules, поиск и фильтры
- Подсказки модулей по таргетам (FileMask, алиасы, приоритет EZTools)
- Сборка автономного KapeIR.Triage (GUI + silent); **selective Modules\bin** — в пакет только нужные парсеры
- Вкладка **«Утилиты»** — инвентаризация `Modules\bin`
- **Перечитать каталог** / **Обновить с GitHub…** (KapeFiles, EZ Tools, Chainsaw)
- Дерево compound, готовые сборки (локальные compounds + sidecar)
- Опциональная Authenticode-подпись (`KAPEPACK_SIGN_CERT`)

---

## Структура исходников

```
src/KapeIR.Core/         — каталог, export, sync, Triage pipeline
src/KapeIR.Builder/      — WPF UI → KapeIR.exe
src/KapeIR.Triage/       — stub → KapeIR.Triage.exe (вшивается в Builder)
src/KapeIR.Ui/           — общая WPF-тема
tests/KapeIR.Builder.Tests/
publish.ps1 / KapeIR.sln — сборка одного EXE
```

Исходники в репозитории для прозрачности и CI; для работы достаточно файла из Releases.

---

## Сборка из исходников

```powershell
.\publish.ps1
# → dist\KapeIR.exe
```

Опционально: `-AlsoPublishRunner` (отдельный `KapeIR.Triage.exe`), `-FrameworkDependent`.
