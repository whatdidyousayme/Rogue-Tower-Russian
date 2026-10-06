# Сборка и проверка для разработчиков

## Сборка

Нужны Python 3.11+, PyInstaller и установленная игра. Компилятор .NET Framework берётся из Windows. Архив `bepinex_core.zip` должен лежать в корне проекта; требуется полный пакет BepInEx 5.4.23.3 для Windows x86.

Скачайте [BepInEx 5.4.23.3 x86](https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x86_5.4.23.3.zip) и сохраните его как `bepinex_core.zip` без распаковки. Установите зависимости: `python -m pip install pyinstaller`. Путь к игре можно передать через `--game` или переменную `ROGUE_TOWER_DIR`.

```powershell
python build_mod32.py --game "E:\SteamLibrary\steamapps\common\Rogue Tower"
python mod_installer/build_installer.py --game "E:\SteamLibrary\steamapps\common\Rogue Tower"
```

DLL создаётся в `build/RogueTowerRussian.dll`, EXE — в `mod_installer/dist`. Сборка не устанавливает мод в игру. Пакет формируется из исходников этого проекта, поэтому состояние папки игры не влияет на комплектность установщика.

## Проверки для разработчика

```powershell
python tools/test_installer.py
python tools/audit_translations.py
```

Первый скрипт проверяет чистую установку, повторную установку, восстановление неполного BepInEx, ошибочный комплект, разрядность и откат после сбоя. Второй извлекает текстовые кандидаты из Assembly-CSharp и сериализованных ресурсов игры (нужен UnityPy). Кандидаты включают внутренние имена объектов и методов: их наличие в отчёте само по себе не означает пропуск в интерфейсе. `tools/CheckTranslations.cs` проверяет тот же движок перевода без Unity, включая динамические числа и rich text.

Клавиша F9 сохраняет диагностический отчёт размеров надписей, включая неактивные элементы в `BepInEx/config/RogueTowerRussian.layout.json`. Измерения выполняются одним размером шрифта, чтобы не переполнять динамический атлас Unity.

`mod_installer/INSTALLATION.txt` — инструкция для архива установщика. Сборка копирует её в `mod_installer/dist/README.txt`; папки `build` и `dist` в Git не хранятся.
