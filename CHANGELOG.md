# Changelog

## [1.2.0] — 2026-08-25

### Security
- **Zip-slip** защита при распаковке (GitHub sync + PackRunner) через общий `SafeZip`
- `SyncResult.Ok` только при отсутствии ошибок
- GitHub sync: **dry-run** перед записью, **backup** перезаписей в `PackBuilder\sync_backup\`, запоминание **SHA256 ZIP**
- Автономный EXE: sidecar **`.sha256`** рядом с файлом

### Architecture
- Domain вынесен в **`KapePack.Core`** (catalog, export, sync, collisions, SafeZip, KapepackPayload, sessions, FileHash)
- UI: **`IDialogService`**; `MainViewModel` распилен на partials (Catalog / Sync / Package / Build) + отдельные row/tree VM
- Сборка пакета в `Task.Run` с **progress / отмена** (`IsBuilding`); GitHub sync — `await` + **отмена**
- Скачивание ZIP потоком на диск; inject `HttpMessageHandler` для тестов
- Поиск KAPE root: `LastKapeRoot`, `KAPE_ROOT`, родители BaseDirectory

### PackRunner
- Перед сбором: выбор диска / `--tsource`, кнопка «Начать сбор»
- «Сохранить лог…» и «Остановить» (kill kape.exe)

### Builder UX
- Сессии: сохранить / загрузить в `PackBuilder\sessions\`
- Sync: превью изменений + samples, затем apply с backup

### Release tooling
- `publish.ps1`: опциональный Authenticode (`KAPEPACK_SIGN_CERT` / `KAPEPACK_SIGN_PASSWORD`)

### Quality
- Unit-тесты: SafeZip, NameCollision, PackageExporter, KAPEPACK footer/extract, mocked GitHub sync (dry-run/backup/hash), sessions, FileHash
- Lab-тесты: `SkippableFact` + `KAPE_ROOT` / автопоиск
- CI: `.github/workflows/ci.yml` (build, test без сети, publish artifacts)

## [1.1.0] — 2026-08-03

### Автономный пакет
- **GUI PackRunner** вместо двух консолей: одно окно с журналом, прогрессом и кнопкой «Открыть RESULTS»
- Запуск `kape.exe` напрямую (без bat/ps1), UAC через manifest
- Кодировка лога: OEM (кириллица в путях)
- Прогресс: процент и статус копирования / модулей PowerShell
- Сборщик отклоняет устаревший console-stub и показывает путь к GUI-stub

### Pack Builder UI
- Иконка приложения
- Переключатель дерева compound: **Таргеты / Модули** (вместо неочевидной галочки)
- Исправлено выделение строк списка (читаемый тёмный стиль)
- Клик по строке таргета/модуля включает элемент в пакет
- Синхронизация галочек без полной пересборки списка

### GitHub sync
- Сообщение показывает **добавлено / обновлено / без изменений** (сравнение содержимого, а не «все файлы скопированы»)

## [1.0.0] — 2026-08

- Первый публичный релиз: Pack Builder, подсказки модулей, автономный EXE (console stub), Releases
