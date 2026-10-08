# Обзор

**KapeIR** — обёртка над KAPE для сборки полевых пакетов и запуска сбора.

## Два приложения

- **KapeIR (Builder)** — на машине аналитика: каталог Targets/Modules, состав пакета, sync инструментов, сборка автономного EXE.
- **Полевой EXE (KapeIR.Triage)** — на целевом хосте: оценка объёма (`--sim`), полный сбор, журнал, файлы chain-of-custody. **Справки в Triage нет** — пакет может оказаться у посторонних.

## KAPE Targets и Modules

- **Target (`.tkape`)** — что копировать с диска (Event Logs, Prefetch, Registry и т.д.).
- **Module (`.mkape`)** — что запускать после копирования (парсеры, Chainsaw, Hayabusa).
- Compound-таргеты/модули — наборы ссылок на leaf-элементы; в каталоге Builder видны все категории (Antivirus, Apps, Browsers, Compound, Logs, Windows, EZTools и др.).

Собранный пакет кладёт выбранные Targets/Modules рядом с `kape.exe` (или встраивает их в standalone EXE).
