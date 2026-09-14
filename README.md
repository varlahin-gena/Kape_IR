# Kape_IR — KAPE Pack Builder

Десктопная GUI-утилита (C# / WPF) для сборки triage-пакетов на базе [KAPE](https://www.kroll.com/en/services/cyber-risk/incident-response-litigation-support/kroll-artifact-parser-extractor-kape): выбор Targets/Modules, подсказки парсеров, обновление каталога и **автономный CollectPack EXE** для сбора на целевой системе.

Интерфейс на русском. Термины KAPE (`tsource`, `--zip`, `--sim`, compound, leaf, `.tkape` / `.mkape`) сохранены.

**Версия: 1.8.3**

> В этом репозитории **нет** дистрибутива KAPE (`kape.exe`, `Targets`, `Modules`). Нужна отдельная установка KAPE рядом с Builder.

---

## Скачать

Готовый файл — в разделе **[Releases](https://github.com/varlahin-gena/Kape_IR/releases/latest)**:

| Файл | Назначение |
|------|------------|
| **`KapePackBuilder.exe`** | Единственный нужный файл. Stub Runner встроен — отдельный `KapePackRunner.exe` не требуется. |
| `KapePackBuilder.exe.sha256` | Контрольная сумма SHA-256 |

Проверка: `Get-FileHash .\KapePackBuilder.exe -Algorithm SHA256`

---

## Что это за система

**Pack Builder** — рабочее место аналитика IR / DFIR: из полного каталога KAPE собирается полевой пакет без ручной возни с CLI и копированием деревьев.

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
| **Two-phase IR** | Phase 1 volatile → Phase 2 disk (RFC 3227), Case ID, манифесты |

KAPE остаётся движком сбора; Builder — оболочка над каталогом и упаковкой.

---

## Требования

- Windows x64
- Установленный / распакованный **KAPE** (`Targets`, `Modules`, желательно `kape.exe`) — путь указывается в Builder
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

**Не** оставляйте активный `_kape.cli` рядом с `kape.exe` при запуске CollectPack — KAPE тогда игнорирует CLI (в т.ч. `--sim`).

### Двухфазный IR (VolatileFirst)

1. **Фаза 1** — volatile → `RESULTS\<host>\Phase1_Volatile`
2. **Фаза 2** — выбранные таргеты / модули → `Phase2_Disk`
3. В конце: `collection_log.txt`, `evidence_manifest.sha256`, `chain_of_custody.txt`

Для дампа памяти нужен signed **WinPmem** как `Modules\bin\winpmem.exe` (см. `Velocidex_WinPmem.mkape`). Без него — `--skip-memory` или код выхода 2.

---

## Возможности Builder

- Виртуализированные списки Targets / Modules, поиск и фильтры
- Подсказки модулей по таргетам (FileMask, алиасы, приоритет EZTools)
- Сборка автономного CollectPack (GUI + silent)
- Вкладка **«Утилиты»** — инвентаризация `Modules\bin`
- Обновление Targets/Modules, EZ Tools и Chainsaw («Обновить…»)
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
- Targets/Modules обычно из сообщества [Eric Zimmerman / KapeFiles](https://github.com/EricZimmerman/KapeFiles) и обновляются кнопкой sync в Builder
- Этот инструмент — обёртка для IR-сборки пакетов; соблюдайте лицензии KAPE и сторонних бинарников в `Modules\bin`

---

## История версий

См. [CHANGELOG.md](CHANGELOG.md).
