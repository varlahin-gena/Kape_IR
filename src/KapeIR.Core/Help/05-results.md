# Папка RESULTS

Типичный корень: `RESULTS\<имя_хоста>\` (подстановка `%m` в KAPE).

## Артефакты KAPE

- **ZIP** (если включён) — упакованные копии + логи; после успешного zip сырые копии часто удаляются.
- **CopyLog.csv** — что скопировано / симулировано.
- **SkipLog** — пропуски.
- **ConsoleLog.txt** — stdout KAPE (и дочерних модулей).

## ModuleOutput

Вывод модулей, например:

- `ModuleOutput\EventLogs\` — CSV Chainsaw (`sigma.csv`, …) и Hayabusa (`hayabusa_*.csv`).

Имена зависят от выбранных `.mkape` и их `%destinationDirectory%`.

## Журнал Triage

Рядом с пакетом / RESULTS также пишется `kape_run_*.txt` — полный журнал UI (оценка + сбор + wrap-up).
