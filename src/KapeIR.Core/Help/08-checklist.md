# Чеклист после сбора

- □ Case ID задан; есть `collection_log.txt` и `chain_of_custody.txt`
- □ Часовой пояс хоста записан (Id / Display / UTC offset / DST) — для таймлайна
- □ `evidence_manifest.sha256` сверен; для дампа памяти — `memory_hash.sha256` (дамп в манифесте не дублируется)
- □ CopyLog / SkipLog / ConsoleLog без критичных пропусков
- □ Фаза 1 (оперативный сбор) до тяжёлого диска; `--skip-memory` — осознанный выбор
- □ `findings_template.csv` → `findings.csv`; строки EXAMPLE заменены
- □ Lab при необходимости: Volatility3_Triage / Hayabusa_Offline / hayabusa_IocCandidates (IOC = unverified)
- □ Передача evidence: заполнить Transfer / Storage в `chain_of_custody.txt`
