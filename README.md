# KAPE Pack Builder

Десктопная GUI-утилита (C# / WPF) для сборки и доработки полных triage-пакетов KAPE:
compound-таргеты + модули + скрипты запуска + **автономный EXE** для сбора на целевой системе.

Интерфейс на русском. Термины KAPE (`tsource`, `--zip`, compound, leaf, имена `.tkape`/`.mkape`) сохранены.

## Скачать (готовые EXE)

Берите сборку из раздела **[Releases](https://github.com/varlahin-gena/KapePackBuilder/releases/latest)**:

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
- **Подсказки модулей по таргетам** — FileMask, Windows-алиасы, эвристики; приоритет EZTools
- **Сборка автономного EXE** — один файл: распаковка + `kape.exe` + targets/modules (+ опционально `Modules\bin`) + автозапуск сбора
- Дерево compound, загрузка существующих compound / `package.json`
- Обновление Targets/Modules с [EricZimmerman/KapeFiles](https://github.com/EricZimmerman/KapeFiles)

## Типичный сценарий
1. Указать корень KAPE (каталог с `Targets`, `Modules` и желательно `kape.exe`)
2. Отметить таргеты (или загрузить compound)
3. При необходимости **Подсказать по таргетам**
4. Нажать **Собрать автономный EXE** — получите один `.exe` для запуска на целевой системе от администратора

## Примечания
- Локальные custom-таргеты сохраняются при синхронизации с GitHub
- Имена compound без авто-префикса `!` (чтобы CMD/запуск не ломали `--target`)
- `Modules\bin` подключается опцией при сборке автономного пакета

## Структура репозитория
```
src/KapePackBuilder/     — WPF-приложение
src/KapePackRunner/      — stub для автономных EXE
tests/                   — unit-тесты
publish.ps1              — публикация single-file EXE
```
