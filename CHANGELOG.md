# Changelog

## [1.8.3] — 2026-09-13

### Architecture / tests
- `PackageDependencyCopier` + `PackageRuntimePacker` выделены из `PackageExporter`
- `CollectPackProgressParser` в Core; Runner GUI тонкий поверх него
- `CollectionRunner` перенесён в Core (тесты с fake `runKape`: `--sim` skip P1, winpmem exit 2)
- Кеш `IncludingCompounds` на время жизни каталога после `Refresh`
- `KapeCatalog.Find`: lookup по basename (`Modules\…\Foo.mkape` → `Foo.mkape`) — чинит ModuleBinGate на путях с папками
- Версии / User-Agent sync/EZ/Chainsaw = 1.8.3
- Тесты YAML quoting / ModuleBinGate расширены; стабилизирован `ModuleBinGate_SkipsMissingThirdParty`

## [1.8.2] — 2026-09-13

### Fix
- Compound YAML: кавычки для Path/Executable с `!` / `!!` (иначе `!!ToolSync.mkape` → `unresolved tag`)
- CollectPack: Sync/ToolSync модули не включаются в triage-пакет (не для сбора, ломают валидацию)
- Диалог сборки: предупреждение Phase2 о пропущенных модулях больше не дублируется
- WinPmem: BinaryUrl → signed `go-winpmem` + CLI `acquire`; предупреждение если в bin лежит unsigned mini
- LiveResponse: `quser`/`query` через PowerShell-fallback (Windows Home)
- Hayabusa v4.1: `dfir-timeline`, `-U` вместо `--UTC` / `csv-timeline`
- Hindsight: `Run-Hindsight.ps1` (PYTHONUTF8) против UnicodeEncodeError на cp1251
- CollectPack GUI: прогресс по фазам (P1 ≈0–15%, P2 ≈15–99%); пульс во время копирования (KAPE не пишет per-file %)
- CollectPack GUI: источник сбора — только выбор буквы диска; папка RESULTS без изменений
- LiveResponse: `Invoke-Utf8Capture.ps1` — сырые байты stdout → UTF-8 BOM (UTF-8 / CP866 / CP1251) для ipconfig/net/netsh/sc/schtasks

## [1.8.1] — 2026-09-13

### Export / CollectPack / UX
- CollectPack: при `--sim` не запускаются парсеры и ZIP; Phase 1 (volatile) пропускается — только оценка targets
- CollectPack GUI: прогресс копирования 5–80%, модули 83–97%, сжатие 98%
- CollectPack GUI: убран кастомный шаблон ComboBox (падение при выборе диска); тёмный список через ComboBoxItem + SystemColors
- Сборка автономного EXE: промежуточная папка пакета удаляется после упаковки (на выходе EXE + `.sha256`, опционально ZIP)
- Прогресс копирования `Modules\bin` при экспорте пакета
- Cancel cleanup temp для sync / EZTools / Chainsaw (`TempCleanup`)
- CollectPack: `--verify` / sidecar `.sha256` перед сбором; `publish.ps1` пишет SHA-256 sidecar (+ опциональный Authenticode)
- Round-trip тест: package.json → phases → CLI args; тесты verify sidecar
- About / кнопка **Журнал** — открытие папки AppLog
- `CatalogUiHelpers` — фильтры/дерево вынесены из `MainViewModel.Catalog`

### Namespaces
- Core: `KapePack.Core.Models` / `Services` / `Shared` (UI-диалоги остаются в `KapePackBuilder.Services`)

## [1.8.0] — 2026-09-12

### Каталог, архитектура, тесты
- Кеш парсинга `.tkape`/`.mkape` по mtime+size (повторный Reload без полного YAML)
- Workspaces: `CatalogWorkspace`, `PackageFormMapper`, `ToolkitUpdateWorkspace`
- `CollectPackPrepare` + тесты exit codes 2/3/0 без сети
- Флакующий live-GitHub sync-тест заменён mock-ом

## [1.7.9] — 2026-09-12

### Корень KAPE и портативный CollectPack
- Builder всегда работает только с корнем из поля сверху: каталог, sync, sessions, exports, stub/cache под `PackBuilder\`
- `kape.exe` берётся только из корня (не из `PackBuilder\exports\…` — устранена коллизия)
- CollectPack: каталог запуска = папка EXE (флешка/шара/диск); cwd и RESULTS рядом; staging ZIP на том же томе, не в `%TEMP%` на C:

## [1.7.8] — 2026-09-12

### Hardening (P0/P1 review)
- Export: подтверждение перед удалением существующей папки пакета (`overwriteExisting`)
- Документация: открываются только `http`/`https`
- Evidence manifest: не хеширует повторно RAM-дампы (`*.raw` и др.) — см. `memory_hash.sha256`
- «Обновить…»: ZIP KapeFiles кешируется после проверки и переиспользуется при apply
- Общий `LaunchManifestIo` / `KapeProcessHost` / `CollectPackCliOptions`; `AppLog`; DI в `App`
- Stub Runner извлекается в LocalAppData; единый `Directory.Build.props` (1.7.8)
- Cancel сборки пытается убрать неполный пакет/EXE

## [1.7.7] — 2026-09-12

### Панель пакета — упрощение чекбоксов
- Убраны **Очистить вывод** (`--flush` больше не выставляется из UI)
- Убрано **Также установить в KAPE** (это не sync с GitHub: ставило бы ваш compound в локальный каталог; sync уже обновляет stock)
- Убрано **Включить Modules\bin**: включается автоматически при two_phase или если выбраны модули

## [1.7.6] — 2026-09-12

### UX вкладок
- **Готовые пакеты**: колонки «Роль», «Детей», «Используется в» (вложенность compound в другие пакеты)
- Вкладка **«Бинарники»** переименована в **«Утилиты»**; подсказка про роль в сборке/на цели
- «Только общие» в дереве: tooltip — leaf, входящие в несколько compound

## [1.7.5] — 2026-09-12

### Builder — убраны сценарии IR
- Удалены ComboBox «Сценарий», кнопка «Применить» и пресеты `ScenarioPresets`
- Выбор Targets/Modules — вручную (и «Подсказать по таргетам»); гипотезы IR остаются в скилле
- **Двухфазный IR (VolatileFirst)** сохранён: чекбокс, Case ID, CollectPack `--phase` / `--skip-memory`

## [1.7.4] — 2026-09-12

### Метки источника (GitHub vs локальный)
- После «Обновить…» (в т.ч. dry-run) пишется `PackBuilder/last_kapefiles_paths.txt`
- В списках Targets/Modules и «Готовые пакеты»: колонка **Источник** (`GitHub` / `локальный` / `?`)
- В дереве compound: теги `[GH]` / `[лок.]` / `[?]`
- Сессии помечены как локальные; без инвентаря путей — эвристика по Author `KAPE Pack Builder…`

## [1.7.3] — 2026-09-12

### Builder — пресеты (triaging-security-incident)
- `av-edr-logs` — Antivirus + EventLogs (диск)
- `ransomware` — EvidenceOfExecution + ProgramExecution + EventLogs; Hayabusa_Offline (two_phase)
- `credential-access` — RegistryHives + EventLogs + DeveloperCloudCredentials; Hayabusa_Offline (two_phase)
- `messaging-p2p` — MessagingClients + P2PClients + TorrentClients (two_phase)
- `vpn-remote` — VPNClients + RemoteAdmin + EventLogs-RDP (two_phase)
- `ai-dev-tools` — AICodingAgents (two_phase)

## [1.7.2] — 2026-09-12

### Chainsaw — Offline compound
- Leaf: `Chainsaw_High` (high/critical + stable), `Chainsaw_Gaps` (analyse gaps)
- Compound `Chainsaw_Offline`: full `Chainsaw` hunt + gaps
- Пресет `evtx-hunt` → `Hayabusa_Offline` **+** `Chainsaw_Offline` (детекции Sigma + timeline/metrics)

## [1.7.1] — 2026-09-12

### Hayabusa — Live vs Offline
- Compound `Hayabusa` = **live** only (документация уточнена); alias `Hayabusa_Live`
- Новый compound `Hayabusa_Offline`: offline timeline + logon + eid/computer/log metrics + pivot-keywords
- Leaf: `hayabusa_ComputerMetrics`, `hayabusa_LogMetrics`, `hayabusa_PivotKeywords`, `hayabusa_OfflineEventLogs_High`
- Пресеты `evtx-hunt` и `fileless-ps` → `Hayabusa_Offline` (не live)

## [1.7.0] — 2026-09-12

### VolatileFirst — скилл collecting-volatile-evidence
- Leaf: `Windows_RegRunLive`, `Windows_ServicesLive`, `SysInternals_ListDlls`
- `LiveResponse_SystemSnapshot`: Run keys + sc queryex; TCP/UDP CSV перенесён в `LiveResponse_NetworkDetails`
- `LiveResponse_ProcessDetails`: ListDLLs; `PowerShell_ProcessList_WMI` без per-file SHA-256 (быстрее на live)
- `Velocidex_WinPmem`: сразу после дампа пишет `memory_hash.sha256` (raw kape.exe)
- CollectPack: fail-fast если нет `winpmem.exe` и не указан `--skip-memory`
- Пресеты: `fileless-ps`; execution / browsers / server / cloud-usb / persistence → `TwoPhase=true`

## [1.6.0] — 2026-09-12

### Builder — сценарии IR
- ComboBox **«Сценарий»** + кнопка **«Применить»** на панели пакета
- Пресеты по скиллу `triaging-windows-with-kape`: triage, IR two_phase, SANS, execution, lateral, browsers, server, cloud/USB, persistence, live-volatile, EVTX/Hayabusa
- Пропуск отсутствующих Targets/Modules с предупреждением; two_phase без дисковых таргетов разрешён для сборки (только Phase 1)

## [1.5.0] — 2026-09-11

### IR VolatileFirst (two_phase)
- Compound-модули: `VolatileFirst`, `VolatileFirst_NoMemory`, `LiveResponse_SystemSnapshot` + leaf (firewall, query user/session, time, env, schtasks, TCP/UDP CSV)
- Пакет `collection_mode=two_phase`: фаза 1 (modules-only) → фаза 2 (disk targets)
- CollectPack: `--phase 1|2`, `--skip-memory`, `--case-id`; GUI-панель для two_phase
- После сбора: `collection_log.txt`, `evidence_manifest.sha256`, `chain_of_custody.txt`; хеш memdump сразу после фазы 1
- Builder: чекбокс «Двухфазный IR», Case ID; фаза 2 = любые выбранные таргеты + остальные модули

## [1.4.0] — 2026-09-11

### PackRunner
- **Оценка `--sim`:** кнопка «Оценить», чекбокс «перед сбором», silent `--sim-only`

### Delivery
- **Один EXE для пользователей:** `KapePackBuilder.exe` со **встроенным** GUI-stub; отдельный `KapePackRunner.exe` рядом больше не нужен
- `publish.ps1` по умолчанию кладёт в `dist` только Builder (`-AlsoPublishRunner` для отладки)
- «О программе» — версия, путь EXE, статус встроенного stub

### Builder UX
- Вкладка **«Бинарники»**: инвентаризация `Modules\bin`

## [1.3.0] — 2026-09-11

### PackRunner
- **Silent / EDR режим:** `CollectPack.exe --silent --tsource C:` (`--log` опционально)
- Коды выхода: 0=OK, 1=сбой сбора, 2=аргументы/payload, 3=ошибка подготовки
- В пакет пишется **`_kape.cli`** (%%d / %%m) для fleet-запуска `kape.exe` без аргументов

### Builder — единое «Обновить…»
- Одна кнопка проверяет и при необходимости обновляет:
  1. **KapeFiles** (Targets/Modules) с dry-run → apply + backup
  2. **EZ Tools** через Get-ZimmermanTools → `Modules\bin`
  3. **Chainsaw** во вложенность `Modules\bin\chainsaw\` под `Chainsaw.mkape`

### Packaging
- Контейнер сбора по-прежнему **только ZIP** (`--zip`)

## [1.2.0] — 2026-08-25

### Security
- **Zip-slip** защита при распаковке (GitHub sync + PackRunner) через общий `SafeZip`
- `SyncResult.Ok` только при отсутствии ошибок
- GitHub sync: **dry-run** перед записью, **backup** перезаписей в `PackBuilder\sync_backup\`, запоминание **SHA256 ZIP**
- Автономный EXE: sidecar **`.sha256`** рядом с файлом

### Architecture
- Domain вынесен в **`KapePack.Core`**
- UI: **`IDialogService`**; `MainViewModel` partials
- Сборка пакета с **progress / отмена**; GitHub sync — **отмена**

### PackRunner
- Перед сбором: выбор диска / `--tsource`, «Начать сбор», «Сохранить лог…», «Остановить»

### Builder UX
- Сессии в `PackBuilder\sessions\`
- Sync: превью изменений + samples, затем apply с backup

### Release tooling
- `publish.ps1`: опциональный Authenticode (`KAPEPACK_SIGN_CERT`)

### Quality
- Unit-тесты + CI

## [1.1.0] — 2026-08-03

### Автономный пакет
- **GUI PackRunner** вместо двух консолей

### Pack Builder UI
- Переключатель дерева compound: **Таргеты / Модули**

### GitHub sync
- Сообщение показывает **добавлено / обновлено / без изменений**

## [1.0.0] — 2026-08

- Первый публичный релиз
