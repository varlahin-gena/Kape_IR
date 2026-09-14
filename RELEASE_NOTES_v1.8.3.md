## KAPE Pack Builder 1.8.3

Готовый GUI для сборки triage-пакетов KAPE и автономного **CollectPack** (two-phase IR, silent/GUI).

### Скачать
| Файл | Назначение |
|------|------------|
| **KapePackBuilder.exe** | Единственный нужный файл (Runner встроен) |
| KapePackBuilder.exe.sha256 | SHA-256 |

### Важно
- **KAPE не входит** в релиз — нужна отдельная установка; в Builder укажите её корень.
- SmartScreen может предупреждать без Authenticode — это ожидаемо для неподписанной сборки.

### Быстрый старт
1. Запустить `KapePackBuilder.exe`, указать корень KAPE
2. Выбрать Targets/Modules → при IR включить **Двухфазный IR**
3. **Собрать автономный EXE** → запустить CollectPack на целевой системе

Подробности — в [README](https://github.com/varlahin-gena/Kape_IR#readme).
