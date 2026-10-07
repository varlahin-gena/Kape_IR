## KapeIR 1.8.8

Готовый GUI для сборки triage-пакетов KAPE и автономного **KapeIR.Triage** (two-phase IR, silent/GUI).

### Скачать
| Файл | Назначение |
|------|------------|
| **KapeIR.exe** | Единственный нужный файл (~131 МБ, Triage-stub встроен) |
| KapeIR.exe.sha256 | SHA-256 |

### Что нового
- **Переименование:** Builder → `KapeIR`, полевой stub → `KapeIR.Triage`
- Автономный пакет на цели по-прежнему называется как пакет (`MyPack.exe`), внутри — stub Triage
- Настройки мигрируют из старой папки AppData `KapePackBuilder` → `KapeIR`

### Важно
- **KAPE не входит** в релиз — нужна отдельная установка; в Builder укажите её корень.
- SmartScreen может предупреждать без Authenticode — ожидаемо для неподписанной сборки.

Подробности — в [README](https://github.com/varlahin-gena/Kape_IR#readme).
