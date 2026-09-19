# Proposal

## Why

Начать CAD Lens с маленького работающего плагина, который легко понять и проверить в AutoCAD, прежде чем добавлять линзы и интерфейс.

## What Changes

- Добавить проект `src/AutoCAD/CadLens.AutoCAD` в решение `CadLens.slnx`.
- Добавить команду `CADLENS`, которая выводит приветствие в AutoCAD.
- Линзы, интерфейс, лента и установщик в это изменение не входят.

## Capabilities

### New Capabilities

- `autocad-command`: запуск команды `CADLENS` и вывод приветствия пользователю.

### Modified Capabilities

Нет.

## Impact

Новый проект плагина AutoCAD и файл решения. Способ сборки, загрузки и проверки в AutoCAD обсудим на следующих шагах.