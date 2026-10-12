## KapeIR 1.9.0

Готовый GUI для сборки triage-пакетов KAPE и автономного **KapeIR.Triage** (two-phase IR, silent/GUI).

### Скачать
| Файл | Назначение |
|------|------------|
| **KapeIR.exe** | Единственный нужный файл (~132 МБ, Triage-stub встроен) |
| KapeIR.exe.sha256 | SHA-256 |

### Что нового
- **Встроенный каталог KapeIR** (Phase1 + Hayabusa v41): засевается в корень KAPE при выборе / sync
- Колонка **Источник**: GitHub / **KapeIR** / локальный
- **Фаза 1b (Vol3)** опциональна: `--skip-memory-analysis` / «Без пост-анализа памяти» — дамп в 1a остаётся
- Справка Builder: разделы Phase1 и пост-анализ Event Logs (желательно Chainsaw **и** Hayabusa)
- WinPmem / Volatility3 / Phase1 live tools через «Обновить с GitHub…»

### Важно
- **KAPE не входит** в релиз — нужна отдельная установка; в Builder укажите её корень.
- SmartScreen может предупреждать без Authenticode — ожидаемо для неподписанной сборки.

Подробности — в [README](https://github.com/varlahin-gena/Kape_IR#readme) и [CHANGELOG](https://github.com/varlahin-gena/Kape_IR/blob/main/CHANGELOG.md).
