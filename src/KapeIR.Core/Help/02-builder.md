# Builder

## Корень KAPE

Укажите папку с `kape.exe`, `Targets\` и `Modules\`. Если есть только `kape.exe`, Builder создаст недостающие каталоги.

## Каталог

- Перечитать каталог (**F5**) — заново просканировать `.tkape` / `.mkape`.
- Фильтры и поиск по имени; дерево по категориям.
- «Подобрать модули» — подсказки парсеров под выбранные таргеты.

## Пакет

- Добавляйте Targets и Modules в пакет, задайте имя, опции (ZIP, VSC, двухфазный IR, Case ID).
- **Сохранить / загрузить сессию** — состав пакета без полной пересборки.
- **Собрать EXE** (**Ctrl+B**) — автономный полевой stub + payload.

## Обновить с GitHub…

Синхронизация (по подтверждению):

- KapeFiles (Targets/Modules с GitHub)
- EZ Tools
- **Chainsaw** → `Modules\bin\chainsaw\`
- **Hayabusa** + `config` + `rules` (~4500) → `Modules\bin\hayabusa\`
- DFIR-NTFS, RegRipper (при необходимости)

После sync перечитайте каталог и проверьте, что нужные `.mkape` видят бинарники в `Modules\bin\`.
