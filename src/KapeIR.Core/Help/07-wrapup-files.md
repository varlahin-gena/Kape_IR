# Файлы после сбора

KapeIR пишет их в корень RESULTS после успешного сбора (не вывод самого KAPE).

## `collection_log.txt`

Паспорт сбора: Case ID, пакет, режим, хост/пользователь, UTC-окно и длительность, часовой пояс хоста, путь и SHA256 коллектора, итог фаз.

**Зачем:** быстро ответить «что делали на машине» без разбора ConsoleLog.

## `findings_template.csv`

Черновик для аналитика (строки `EXAMPLE`), не автодетект Chainsaw/Hayabusa.

Колонки: Time, Host, Artifact, Finding, Confidence, Evidence path.

**Зачем:** скопировать в `findings.csv` и вручную заносить выводы со ссылками на файлы. Confidence по умолчанию `unverified`.

## `evidence_manifest.sha256`

Строки `SHA256  относительный/путь` по файлам в RESULTS (после ZIP — в основном архив, логи, ModuleOutput CSV, wrap-up).

Не хэширует сам себя и `chain_of_custody.txt`. Большие дампы RAM и массовый MFTECmd `Resident\` пропускает (см. `memory_hash.sha256` / комментарии в манифесте).

**Зачем:** проверить, что evidence не меняли при копировании/передаче.

## `chain_of_custody.txt`

Бланк цепочки хранения: Case ID, окно сбора, TZ, кто собрал, хост, метод, пакет, корень evidence, ссылка на манифест.

Поля **Transfer** / **Storage** — `[TO BE COMPLETED]`: заполнить при передаче в lab/архив.
