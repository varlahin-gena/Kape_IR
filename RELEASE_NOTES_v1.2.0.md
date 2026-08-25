# KAPE Pack Builder 1.2.0

Hardening и полевой UX: безопасный sync, Core library, PackRunner с выбором диска, тесты и CI.

### Что скачать
| Файл | Назначение |
|------|------------|
| `KapePackBuilder.exe` | Основная программа |
| `KapePackRunner.exe` | Stub рядом с Builder (нужен для «Собрать автономный EXE») |

### Главное
- **Zip-slip** защита, честный `SyncResult`, стрим download
- GitHub sync: **dry-run** → preview → apply с **backup** и SHA256 ZIP
- Сборка пакета: progress + **Отмена**; сессии в `PackBuilder\sessions\`
- Автономный EXE пишет sidecar **`.sha256`**
- PackRunner: выбор диска/`--tsource`, сохранить лог, остановить сбор
- Domain в **KapePack.Core**; CI (build/test/publish artifacts)
- `publish.ps1`: опциональный Authenticode (`KAPEPACK_SIGN_CERT`)

### Важно при обновлении с 1.1
1. Замените **оба** EXE из релиза.
2. Пересоберите автономные пакеты, если нужны новые возможности Runner (выбор диска, лог).
3. Stub кладите рядом с Builder (~70 МБ GUI).

Полный список — в `CHANGELOG.md`.
