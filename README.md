# Kape_IR

Десктопная GUI-утилита (C# / WPF) для сборки triage-пакетов на базе [KAPE](https://www.kroll.com/en/services/cyber-risk/incident-response-litigation-support/kroll-artifact-parser-extractor-kape): выбор Targets/Modules, подсказки парсеров, обновление каталога и **автономный CollectPack EXE** для сбора на целевой системе.

Интерфейс на русском. Термины KAPE (`tsource`, `--zip`, `--sim`, compound, leaf, `.tkape` / `.mkape`) сохранены.

**Версия: 1.8.4**

> В этом репозитории **нет** дистрибутива KAPE (`kape.exe`, `Targets`, `Modules`). Нужна отдельная установка KAPE рядом с Builder.

---

## Скачать

Готовый файл — в разделе **[Releases](https://github.com/varlahin-gena/Kape_IR/releases/latest)**:

| Файл | Назначение |
|------|------------|
| **`KapePackBuilder.exe`** | Единственный нужный файл |
| `KapePackBuilder.exe.sha256` | Контрольная сумма SHA-256 |

Проверка: `Get-FileHash .\KapePackBuilder.exe -Algorithm SHA256`

---

## Что это за система

**Kape_IR** — рабочее место аналитика IR / DFIR: из полного каталога KAPE собирается полевой пакет без ручной возни с CLI и копированием деревьев.

```
┌─────────────────────┐     export      ┌──────────────────────┐
│  KapePackBuilder    │ ──────────────► │  CollectPack.exe     │
│  (каталог, выбор,   │   один EXE      │  (на целевой системе)│
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
| **Builder** | Каталог Targets/Modules, фильтры, подсказки модулей, сессии, sync с GitHub KapeFiles, EZ Tools / Chainsaw |
| **CollectPack** | Портативный сборщик: распаковка рядом с EXE, запуск `kape.exe`, GUI-прогресс или silent |
| **Two-phase IR** | Phase 1 volatile → Phase 2 disk (RFC 3227 / Order of Volatility), Case ID, манифесты |

KAPE остаётся движком сбора; Builder — оболочка над каталогом и упаковкой.

---

## Требования

- Windows x64
- Установленный / распакованный **KAPE** (`Targets`, `Modules`, `kape.exe`) — путь указывается в Builder
- Права администратора на целевой системе для полного triage (UAC в CollectPack)

---

## Быстрый старт

1. Скачайте `KapePackBuilder.exe` из Releases
2. Запустите, укажите корень KAPE
3. Отметьте Targets и Modules (при необходимости — **Подсказать по таргетам**)
4. Для IR: включите **Двухфазный IR**, задайте Case ID
5. **Собрать автономный EXE** → на выходе `ИмяПакета.exe` (+ `.sha256`)
6. На целевой: GUI («Оценить» / «Начать сбор») или `CollectPack.exe --silent --tsource C:`

Выбор — из **полного** каталога Targets/Modules (Antivirus, Apps, Browsers, Compound, Logs, P2P, Windows, EZTools, custom), не из одного compound.

---

## CollectPack — режимы

Пакет **портативен**: staging и `RESULTS` рядом с EXE (флешка / шара / диск), не в `%TEMP%` на C:.

| Режим | Запуск |
|------|--------|
| GUI | `CollectPack.exe` → диск → «Оценить» / «Начать сбор» |
| Silent | `CollectPack.exe --silent --tsource C:` |
| Только оценка | `CollectPack.exe --sim-only --tsource C:` |
| Two-phase IR | `CollectPack.exe --silent --tsource C: --case-id IR-001` |
| Только volatile | `CollectPack.exe --silent --tsource C: --phase 1` |
| Без RAM dump | `CollectPack.exe --silent --tsource C: --skip-memory` |

### Двухфазный IR (VolatileFirst)

Порядок по RFC 3227 (Order of Volatility): сначала живые данные, потом диск.

#### Фаза 1 — volatile (`VolatileFirst` → `RESULTS\<host>\Phase1_Volatile`)

Собирает **только live/volatile** evidence (modules-only, до дискового triage):

| Шаг | Модуль | Что снимает |
|-----|--------|-------------|
| 1 | `Velocidex_WinPmem` | RAM-дамп (`memory.raw` / aff4) + `memory_hash.sha256` |
| 2 | `LiveResponse_NetworkDetails` | IP/DNS/ARP/маршрут, netstat, NetBIOS |
| 3 | `LiveResponse_ProcessDetails` | Процессы, дерево, сервисы, handles, injected threads |
| 4 | `LiveResponse_NetSystemInfo` | `net user` / groups / sessions / shares / started services |
| 5 | `LiveResponse_SystemSnapshot` | Время, env, firewall, quser/session, schtasks, Run-keys, services |

Без RAM: модуль `VolatileFirst_NoMemory` или CollectPack `--skip-memory`.  
Для дампа нужен signed **WinPmem** как `Modules\bin\winpmem.exe` (см. `Velocidex_WinPmem.mkape`). Без него — `--skip-memory` или код выхода 2.

#### Фаза 2 — disk (`Phase2_Disk`)

Выбранные в Builder **Targets** и парсеры (**Modules**) — классический KAPE-сбор с диска.

#### После сбора

`collection_log.txt`, `evidence_manifest.sha256`, `chain_of_custody.txt` (с Case ID, если задан).

---

## Возможности Builder

- Виртуализированные списки Targets / Modules, поиск и фильтры
- Подсказки модулей по таргетам (FileMask, алиасы, приоритет EZTools)
- Сборка автономного CollectPack (GUI + silent); **selective Modules\bin** — в пакет только нужные парсеры
- Вкладка **«Утилиты»** — инвентаризация `Modules\bin`
- **Перечитать каталог** / **Обновить с GitHub…** (KapeFiles, EZ Tools, Chainsaw)
- Дерево compound, сессии, `package.json`
- Опциональная Authenticode-подпись (`KAPEPACK_SIGN_CERT`)

---

## Структура исходников

```
src/KapePack.Core/       — каталог, export, sync, CollectPack pipeline
src/KapePackBuilder/     — WPF UI (deliverable)
src/KapePackRunner/      — stub, вшивается в Builder при publish
tests/                   — unit-тесты
publish.ps1              — сборка одного EXE (для разработчиков)
```

Исходники в репозитории для прозрачности и CI; для работы достаточно файла из Releases.

---

## Лицензия и стороннее ПО

- **KAPE** — продукт Kroll; распространяется отдельно, в репозиторий не входит
- Targets/Modules обычно из сообщества [Eric Zimmerman / KapeFiles](https://github.com/EricZimmerman/KapeFiles) и обновляются кнопкой Обновить в Builder
- Этот инструмент — обёртка для IR-сборки пакетов; соблюдайте лицензии KAPE и сторонних бинарников в `Modules\bin`

---

## История версий

См. [CHANGELOG.md](CHANGELOG.md).
