# KAPE Pack Builder

Десктопная GUI-утилита (C# / WPF) для сборки и доработки полных triage-пакетов KAPE:
compound-таргеты + модули + скрипты запуска + портативный ZIP.

Интерфейс на русском. Термины KAPE (`tsource`, `--zip`, compound, leaf, имена `.tkape`/`.mkape`) сохранены.

## Требования
- Windows x64
- Для сборки из исходников: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Сборка одного EXE
```powershell
.\publish.ps1
```
Результат: `dist\KapePackBuilder.exe` (self-contained, один файл).

## Запуск
```
dist\KapePackBuilder.exe
```
Корень KAPE по умолчанию: последний использованный путь, родительская папка EXE или выбор в шапке UI.

## Возможности
- Быстрые виртуализированные списки Targets / Modules с поиском и фильтрами
- **Подсказки модулей по таргетам** — совпадение FileMask, Windows-алиасы (Prefetch→PECmd и др.), эвристики по имени/описанию; приоритет EZTools
- Дерево compound с метками «общих» артефактов и слиянием нескольких пакетов
- Загрузка существующих compound / `package.json`
- Экспорт полного пакета (установка в KAPE, ZIP, копирование зависимостей)
- Обновление Targets/Modules с [EricZimmerman/KapeFiles](https://github.com/EricZimmerman/KapeFiles)

## Типичный сценарий
1. Указать корень KAPE (каталог с `Targets` и `Modules`)
2. Отметить нужные таргеты (или загрузить готовый compound)
3. Нажать **Подсказать по таргетам** и добавить подходящие модули
4. Заполнить имя/описание пакета и нажать **Собрать пакет**

## Примечания
- Локальные custom-таргеты (например `!PSBCollection`) сохраняются при синхронизации с GitHub
- `Modules\bin` не перезаписывается синхронизацией и не копируется автоматически в ZIP
- Флаги CLI KAPE (`--zip`, `--flush`, `--vss`) в UI оставлены как в оригинале

## Структура репозитория
```
src/KapePackBuilder/     — WPF-приложение
tests/                   — unit-тесты
publish.ps1              — публикация single-file EXE
```
