## Kape_IR 1.8.4

Готовый GUI для сборки triage-пакетов KAPE и автономного **CollectPack** (two-phase IR, silent/GUI).

### Скачать
| Файл | Назначение |
|------|------------|
| **KapePackBuilder.exe** | Единственный нужный файл (~131 МБ, Runner встроен) |
| KapePackBuilder.exe.sha256 | SHA-256 |

### Что нового
- **Selective Modules\bin** — в CollectPack только бинарники выбранных модулей
- Меньше размер Builder (~206 → ~131 МБ)
- UI: «Перечитать каталог» / «Обновить с GitHub…»; «О программе» — Kape_IR + версия

### Фаза 1 (VolatileFirst) — что собирает
Live/volatile до дискового triage (`Phase1_Volatile`):
1. **WinPmem** — RAM-дамп (+ hash)
2. **Network** — IP/DNS/ARP/маршрут, netstat, NetBIOS
3. **Processes** — процессы, дерево, сервисы, handles, injected threads
4. **Net system info** — users/groups/sessions/shares
5. **System snapshot** — время, env, firewall, quser, schtasks, Run-keys, services

Без RAM: `--skip-memory` / `VolatileFirst_NoMemory`. Затем **фаза 2** — выбранные Targets/Modules с диска.

### Важно
- **KAPE не входит** в релиз — нужна отдельная установка; в Builder укажите её корень.
- SmartScreen может предупреждать без Authenticode — ожидаемо для неподписанной сборки.

Подробности — в [README](https://github.com/varlahin-gena/Kape_IR#readme).
