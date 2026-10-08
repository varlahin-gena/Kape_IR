# Chainsaw / Hayabusa — минимум

Offline-hunt по Event Logs. Без `.evtx` в RESULTS анализ почти бесполезен.

## 1. Бинарники (`Modules\bin`)

Установите через Builder → **Обновить с GitHub…** (или вручную):

### Chainsaw

`Modules\bin\chainsaw\`

- `Chainsaw.exe`
- `rules\`, `sigma\`, `mappings\` (в т.ч. `sigma-event-logs-all.yml`)

Stock `Chainsaw.mkape` ожидает именно эту вложенность.

### Hayabusa

`Modules\bin\hayabusa\`

- `hayabusa.exe`
- `config\`
- `rules\` (~4500 YAML/YML из полного **win-x64** ZIP релиза, не live-response)

## 2. Targets (обязательно)

В пакет нужен сбор **Windows Event Logs** (файлы `.evtx`, обычно `C:\Windows\System32\winevt\Logs\`).

Примеры (любой достаточный набор, не один compound):

- Leaf / compound с Event Logs в каталоге Targets (категории Windows, Logs, Compound и т.д.)
- Минимально — таргеты, которые реально кладут `.evtx` в RESULTS

Без EVTX Chainsaw/Hayabusa отработают «вхолостую» или с пустым выводом.

## 3. Modules

### Chainsaw

- Модуль **`Chainsaw`** (stock `.mkape`): `hunt` + rules/sigma/mapping → CSV в `ModuleOutput\EventLogs`.

В логе видно: `Loaded N detection rules`. Предупреждения по WDI `UserData.bin` (`Failed to determine entry size`) — **безопасно игнорировать**.

### Hayabusa (CLI 4.1)

Stock GitHub `hayabusa_*.mkape` со старым CLI (`csv-timeline` / `--UTC`) **несовместимы** с Hayabusa 4.x.

Используйте локальные модули под 4.1 (в KAPE-корне, не в upstream GitHub), например:

- `hayabusa_OfflineEventLogs_v41` — `dfir-timeline` (**здесь грузятся rules**)
- `hayabusa_OfflineLogonSummary_v41` — `logon-summary` (**без** detection rules)
- `hayabusa_EventStatistics_v41` — `eid-metrics` (**без** rules)
- compound вроде `Hayabusa_Offline_v41` — удобный набор

Флаги вроде `-q` глушат баннер «сколько rules загружено»; факт работы rules — колонки `RuleTitle` / `RuleID` в CSV timeline.

## 4. Чеклист перед сборкой пакета

1. Sync: Chainsaw + Hayabusa (exe + rules) на месте  
2. В пакете — Target(ы) с Event Logs  
3. В пакете — `Chainsaw` и/или `hayabusa_*_v41`  
4. Собрать EXE / прогнать тест на машине с admin
