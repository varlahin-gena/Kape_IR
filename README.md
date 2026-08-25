# KAPE Pack Builder

Десктопная GUI-утилита (C# / WPF) для сборки и доработки полных triage-пакетов KAPE:
compound-таргеты + модули + скрипты запуска + **автономный EXE** для сбора на целевой системе.

Интерфейс на русском. Термины KAPE (`tsource`, `--zip`, compound, leaf, имена `.tkape`/`.mkape`) сохранены.

**Текущая версия: 1.1.0**

## Скачать (готовые EXE)

Берите сборку из раздела **[Releases](https://github.com/varlahin-gena/KapePackBuilder/releases/latest)** (релиз **v1.1.0**):

| Файл | Назначение |
|------|------------|
| **`KapePackBuilder.exe`** | Запускайте это. Основная программа. |
| **`KapePackRunner.exe`** | Кладите **рядом** с `KapePackBuilder.exe`. Нужен при «Собрать автономный EXE» (не запускать отдельно). |

Для повседневной работы достаточно этих двух файлов. Исходники в репозитории — для разработки и пересборки.

## Требования
- Windows x64
- Рядом должен быть каталог KAPE (`Targets`, `Modules`, желательно `kape.exe`)
- Для сборки из исходников: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Сборка из исходников
```powershell
.\publish.ps1
```
Результат: `dist\KapePackBuilder.exe` и `dist\KapePackRunner.exe`.

## Возможности
- Быстрые виртуализированные списки Targets / Modules с поиском и фильтрами
- Клик по строке или галочка — включение в пакет; тёмная тема выделения
- **Подсказки модулей по таргетам** — FileMask, Windows-алиасы, эвристики; приоритет EZTools
- **Сборка автономного EXE** — один файл с GUI-окном (лог + прогресс + UAC), без двух консолей
- Дерево compound с переключателем **Таргеты / Модули**, фильтр «Только общие»
- Загрузка существующих compound / `package.json`
- Обновление Targets/Modules с [EricZimmerman/KapeFiles](https://github.com/EricZimmerman/KapeFiles) — считаются только реально добавленные/изменённые файлы

## Типичный сценарий
1. Указать корень KAPE (каталог с `Targets`, `Modules` и желательно `kape.exe`)
2. Отметить таргеты (или загрузить compound)
3. При необходимости **Подсказать по таргетам**
4. Нажать **Собрать автономный EXE** — один `.exe`: на целевой системе одно окно с логом (UAC)

## Примечания
- Локальные custom-таргеты сохраняются при синхронизации с GitHub
- Имена compound без авто-префикса `!` (чтобы CMD/запуск не ломали `--target`)
- `Modules\bin` подключается опцией при сборке автономного пакета
- Stub для автономного пакета должен быть GUI (`KapePackRunner.exe` ~70 МБ). Старый console-stub сборщик отклонит

## Структура репозитория
```
src/KapePack.Core/       — domain (catalog, export, sync, zip-safe)
src/KapePackBuilder/     — WPF UI
src/KapePackRunner/      — GUI-stub для автономных EXE (окно + лог)
tests/                   — unit-тесты
.github/workflows/ci.yml — CI: build, test, publish
publish.ps1              — публикация single-file EXE
CHANGELOG.md             — история изменений
```

## История версий
Кратко — в [CHANGELOG.md](CHANGELOG.md). Полные заметки к релизу — во вкладке Releases на GitHub.
