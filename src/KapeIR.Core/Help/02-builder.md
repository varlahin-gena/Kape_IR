# Builder

## Корень KAPE

Укажите папку с `kape.exe`, `Targets\` и `Modules\`. Если есть только `kape.exe`, Builder создаст недостающие каталоги и **засеет встроенный каталог KapeIR** (Phase1 compounds/leaf + Hayabusa v41 + helper-скрипты). Повторный выбор корня / sync при необходимости обновит этот набор (версия маркера в `Modules\Apps\Local\Phase1\`).

## Каталог

- Перечитать каталог (**F5**) — заново просканировать `.tkape` / `.mkape`.
- Верхний поиск — только по **имени**; под заголовками таблиц — фильтры по столбцам и сортировка кликом.
- Колонка **Источник**:
  - **GitHub** — путь был в последнем zip EricZimmerman/KapeFiles;
  - **KapeIR** — first-party (Phase1, Hayabusa v41, сборки Pack Builder с Author `KapeIR`…);
  - **локальный** — есть только локально и не first-party;
  - **?** — пока нет inventory путей (нажмите «Обновить с GitHub…»).
- «Подобрать модули» — подсказки парсеров под выбранные таргеты.
- Вкладка **Модули**: колонка **Bin** (`✓` / `~` / `✗` / `—`) — видит ли `.mkape` файлы в `Modules\bin\`. Фильтр **«Без бинарника»**, кнопка **«Проверить bin»** (сборка или весь каталог).

## Сборка

- Добавляйте Targets и Modules в сборку, задайте имя, опции (ZIP, VSC, **двухфазный IR**, Case ID).
- Двухфазный пакет всегда несёт Phase 1a/1b compounds; на целевой машине 1b можно не запускать. Подробности — раздел **«Двухфазный IR / Phase1»**.
- Вкладка **«Готовые сборки»**: **Загрузить** / **Сохранить** / **Удалить**.
  - Сохранение пишет compound `.tkape` (и `_Modules.mkape`) в каталог KAPE; IR-поля — в `PackBuilder\assemblies\`.
  - Правка сборки с GitHub создаёт **локальную копию** (`…_Local`), upstream не перезаписывается.
  - Удаление — для сборок с источником **KapeIR** или **локальный** (не GitHub / не «?»).
- **Собрать EXE** (**Ctrl+B**) — автономный полевой stub + payload (для two_phase — selective `Modules\bin`, в т.ч. WinPmem / Volatility3 при наличии в корне).

## Обновить с GitHub…

Синхронизация (по подтверждению):

- KapeFiles (Targets/Modules с GitHub) + обновление меток Источник
- EZ Tools
- **Chainsaw** → `Modules\bin\chainsaw\`
- **Hayabusa** + `config` + `rules` (~4500) → `Modules\bin\hayabusa\`
- DFIR-NTFS, RegRipper (при необходимости)
- **Каталог KapeIR** (засев встроенных `.mkape` / скриптов)
- **Phase1 binaries**: WinPmem, Volatility3, live tools (Sysinternals + `Get-*.ps1`) при необходимости

После sync Builder перечитывает каталог и показывает сводку Modules\bin. Колонка **Bin** / фильтр **«Без бинарника»** / **«Проверить bin»** — для точечной проверки.
