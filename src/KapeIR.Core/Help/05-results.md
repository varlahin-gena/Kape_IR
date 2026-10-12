# Папка RESULTS

Типичный корень: `RESULTS\<имя_хоста>\` (подстановка `%m` в KAPE).

## Артефакты KAPE

- **ZIP** (если включён) — упакованные копии + логи; после успешного zip сырые копии часто удаляются.
- **CopyLog.csv** — что скопировано / симулировано.
- **SkipLog** — пропуски.
- **ConsoleLog.txt** — stdout KAPE (и дочерних модулей).

## Phase1_Volatile

Двухфазный пакет (см. **«Двухфазный IR / Phase1»**):

- live-вывод фазы 1a (сеть, процессы, snapshot, `memory.raw`);
- `MemoryAnalysis\` — `Phase1_MemoryTriage`, если 1b не отключён;
- `MemoryDeep\` — `Phase1_MemoryDeep`, при тех же условиях.

Без дампа (`--skip-memory`) 1b не запускается. Без пост-анализа (`--skip-memory-analysis`) дамп снимается, папок Memory* может не быть.

## ModuleOutput

Вывод модулей фазы 2 (под `Phase2_Disk\`), например:

- `ModuleOutput\EventLogs\` — CSV **Chainsaw** и **Hayabusa** (желательно оба; см. «Пост-анализ Event Logs»).

Имена зависят от выбранных `.mkape` и их `%destinationDirectory%`.

## Журнал Triage

Рядом с пакетом / RESULTS также пишется `kape_run_*.txt` — полный журнал UI (оценка + сбор + wrap-up).
