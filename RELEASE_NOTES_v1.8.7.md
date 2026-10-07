## Kape_IR 1.8.7

Готовый GUI для сборки triage-пакетов KAPE и автономного **CollectPack** (two-phase IR, silent/GUI).

### Скачать
| Файл | Назначение |
|------|------------|
| **KapePackBuilder.exe** | Единственный нужный файл (~131 МБ, Runner встроен) |
| KapePackBuilder.exe.sha256 | SHA-256 |

### Что нового
- **Пустой корень с kape.exe** — Builder сам создаёт `Targets` / `Modules` / `Modules\bin`
- **Первый запуск** — нет двойных ошибок «нет Targets», пока не выбрали папку KAPE («Обзор…»)
- Builder не обязан лежать внутри каталога KAPE

### Важно
- **KAPE не входит** в релиз — нужна отдельная установка; в Builder укажите её корень.
- SmartScreen может предупреждать без Authenticode — ожидаемо для неподписанной сборки.

Подробности — в [README](https://github.com/varlahin-gena/Kape_IR#readme).
