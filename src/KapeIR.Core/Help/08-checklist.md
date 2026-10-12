# Чеклист после сбора

- □ Case ID задан; есть `collection_log.txt` и `chain_of_custody.txt`
- □ Часовой пояс хоста записан (Id / Display / UTC offset / DST) — для таймлайна
- □ `evidence_manifest.sha256` сверен; для дампа памяти — `memory_hash.sha256` (дамп в манифесте не дублируется)
- □ CopyLog / SkipLog / ConsoleLog без критичных пропусков
- □ Фаза 1a (оперативный сбор) до диска; `--skip-memory` — осознанный выбор (без дампа)
- □ KapeIR каталог (Phase1 + Hayabusa v41) засеян Builder’ом; WinPmem + Volatility3 в `Modules\bin` (если нужен 1b / дамп)
- □ Builder: колонка **Источник** / **Bin** / «Проверить bin» — KapeIR-модули на месте, парсеры видят `Modules\bin`
- □ Фаза 1b: по умолчанию вкл.; на целевой машине можно `--skip-memory-analysis` / «Без пост-анализа» (дамп остаётся)
- □ `findings_template.csv` → `findings.csv`; строки EXAMPLE заменены
- □ Фаза 2 (Event Logs): желательно **и** `Chainsaw`, **и** `Hayabusa_Offline_v41` (см. «Пост-анализ Event Logs»); IOC = unverified до сверки
- □ Передача evidence: заполнить Transfer / Storage в `chain_of_custody.txt`
