# Пост-анализ Windows Event Logs

Offline-hunt по собранным `.evtx` (обычно фаза 2 / `ModuleOutput\EventLogs`). Без Event Logs в RESULTS оба инструмента бесполезны.

## Зачем оба: Chainsaw и Hayabusa

**Желательно включать оба** в один пакет (или один compound-набор + `Chainsaw`), а не выбирать «или / или».

| | Chainsaw | Hayabusa (KapeIR v41) |
|--|----------|------------------------|
| Сильная сторона | Sigma hunt по EVTX, удобный CSV под SIEM/разбор | DFIR-timeline, logon-summary, eid-metrics; богатый набор правил Yamato |
| Типичный вывод | `ModuleOutput\EventLogs\` (CSV hunt) | timeline / logon / metrics CSV (+ JSONL у timeline) |
| Пересечение | Часть срабатываний дублируется | Другая часть правил и углы обзора другие |

Перекрытие ложных/истинных срабатываний — норма: сверяйте оба CSV и заносите в `findings.csv` с `Confidence` (часто `unverified`, пока не подтвердили артефактом). Один инструмент не заменяет второй.

Минимальный набор модулей в сборке:

1. **`Chainsaw`** (stock GitHub)  
2. **`Hayabusa_Offline_v41`** (KapeIR) — или leaf’ы `hayabusa_OfflineEventLogs_v41` + logon + eid-metrics  

Опционально: `hayabusa_OfflineEventLogs_High_v41` (фокус high/critical), live `hayabusa_LiveResponse_v41` (не замена offline по собранным EVTX).

## 1. Бинарники (`Modules\bin`)

Builder → **Обновить с GitHub…**:

### Chainsaw — `Modules\bin\chainsaw\`

- `Chainsaw.exe`
- `rules\`, `sigma\`, `mappings\` (в т.ч. `sigma-event-logs-all.yml`)

Stock `Chainsaw.mkape` ждёт именно эту вложенность.

### Hayabusa — `Modules\bin\hayabusa\`

- `hayabusa.exe`
- `config\`
- `rules\` (~4500 YAML/YML из полного **win-x64** ZIP, не live-response)

## 2. Targets (обязательно)

В пакет — сбор **Windows Event Logs** (`.evtx`, обычно `C:\Windows\System32\winevt\Logs\`).

Берите достаточный набор из каталога (Windows / Logs / Compound и т.д.) — не один «любимый» compound в ущерб охвату. Без EVTX в RESULTS Chainsaw/Hayabusa отработают вхолостую.

## 3. Modules

### Chainsaw

- **`Chainsaw`**: `hunt` + rules/sigma/mapping → CSV в `ModuleOutput\EventLogs`.

В логе: `Loaded N detection rules`. Предупреждения по WDI `UserData.bin` (`Failed to determine entry size`) — **безопасно игнорировать**.

### Hayabusa (CLI 4.1, модули KapeIR)

Stock GitHub `hayabusa_*.mkape` (`csv-timeline` / `--UTC`) **несовместимы** с Hayabusa 4.x. Нужны модули с источником **KapeIR** (засев Builder’ом):

| Модуль | Назначение |
|--------|------------|
| `hayabusa_OfflineEventLogs_v41` | `dfir-timeline` — **здесь грузятся detection rules** |
| `hayabusa_OfflineLogonSummary_v41` | `logon-summary` (без detection rules) |
| `hayabusa_EventStatistics_v41` | `eid-metrics` (без rules) |
| `Hayabusa_Offline_v41` | compound: timeline + logon + eid-metrics |

`-q` глушит баннер «сколько rules»; факт работы rules — колонки `RuleTitle` / `RuleID` в CSV timeline.

## 4. Чеклист перед сборкой пакета

1. Sync: и Chainsaw, и Hayabusa (exe + rules) на месте; колонка **Bin** без `✗`  
2. В пакете — Target(ы) с Event Logs  
3. В пакете — **`Chainsaw` и `Hayabusa_Offline_v41`** (оба)  
4. Собрать EXE / тест от admin; в RESULTS смотреть оба набора CSV
