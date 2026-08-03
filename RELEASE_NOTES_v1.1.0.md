# Release v1.1.0 — черновик для GitHub Release

Когда будете готовы опубликовать (после push):

```powershell
# из PackBuilder.Net после publish.ps1
gh release create v1.1.0 `
  --title "KAPE Pack Builder 1.1.0" `
  --notes-file RELEASE_NOTES_v1.1.0.md `
  .\dist\KapePackBuilder.exe `
  .\dist\KapePackRunner.exe
```

---

## KAPE Pack Builder 1.1.0

Одно окно вместо двух консолей при запуске автономного triage-пакета, плюс правки UI и честный отчёт синхронизации с GitHub.

### Что скачать
| Файл | Назначение |
|------|------------|
| `KapePackBuilder.exe` | Основная программа |
| `KapePackRunner.exe` | Stub рядом с Builder (нужен для «Собрать автономный EXE») |

### Главное
- Автономный EXE открывает **GUI** с логом и прогрессом (UAC)
- Иконка приложения
- Дерево compound: радиокнопки Таргеты / Модули
- Списки Targets/Modules: нормальное выделение, клик по строке = в пакет
- GitHub sync: счётчики только реально новых/изменённых файлов

### Важно при обновлении с 1.0
1. Замените **оба** EXE из релиза.
2. Пересоберите автономные пакеты — старые EXE со console-stub внутри не обновятся сами.
3. Stub должен быть GUI (~70 МБ). Если рядом лежит старый ~10 МБ console `KapePackRunner.exe` — удалите его.

Полный список изменений — в `CHANGELOG.md`.
