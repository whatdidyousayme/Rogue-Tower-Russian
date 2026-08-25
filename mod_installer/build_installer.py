# -*- coding: utf-8 -*-
"""Сборка установщика мода в один .exe через PyInstaller."""
import os
import subprocess
import sys
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))
# Папка игры = уровень выше mod_installer/ (при необходимости — переменная окружения ROGUE_TOWER_DIR).
GAME = os.environ.get('ROGUE_TOWER_DIR', os.path.dirname(HERE))
PLUGINS = os.path.join(GAME, 'BepInEx', 'plugins')
DIST = os.path.join(HERE, 'dist')

def main():
    # 1) Собираем .exe (onefile, с окном --noconsole не надо? --console для логов, но для GUI --noconsole лучше)
    #    Для пользователя GUI-установщика без консоли подойдёт --noconsole, но тогда ошибки не видны.
    #    Выберем --noconsole (чистый GUI), при этом print() безвредны.
    # Иконка в стиле игры.
    icon = os.path.join(DIST, 'icon.ico')
    if not os.path.exists(icon):
        icon = os.path.join(HERE, 'icon.ico')
    icon_args = ['--icon', icon] if os.path.exists(icon) else []

    # Встроенный архив BepInEx (автономность: установка без интернета).
    bep_zip = None
    for cand in (os.path.join(DIST, 'bepinex_core.zip'), os.path.join(HERE, 'bepinex_core.zip')):
        if os.path.exists(cand):
            bep_zip = cand
            break
    add_data = []
    if bep_zip:
        # PyInstaller --add-data "src;dest" (на Windows разделитель ';').
        add_data = ['--add-data', bep_zip + ';.']
        print('  +  встраиваем BepInEx:', os.path.basename(bep_zip))

    # Встраиваем иконку, чтобы она попадала в sys._MEIPASS (для иконки окна).
    ico = os.path.join(DIST, 'icon.ico')
    if os.path.exists(ico):
        add_data = add_data + ['--add-data', ico + ';.']
        print('  +  встраиваем иконку окна:', os.path.basename(ico))

    # Встраиваем файлы мода, чтобы установщик был полностью автономным (один .exe):
    # пользователю не нужно держать dll/словарь/исходники рядом с установщиком.
    mod_bundle = [
        (os.path.join(PLUGINS, 'RogueTowerRussian.dll'), 'RogueTowerRussian.dll'),
        (os.path.join(PLUGINS, 'translations.json'), 'translations.json'),
    ]
    # Исходники (открытость мода): ищем в mod_translator, затем в корне игры.
    for f in ('TranslatorPlugin.cs', 'build_mod32.py', 'changelog.txt'):
        src = next((x for x in (os.path.join(GAME, 'mod_translator', f), os.path.join(GAME, f))
                    if os.path.exists(x)), None)
        if src:
            mod_bundle.append((src, f))
    for src, _ in mod_bundle:
        if os.path.exists(src):
            add_data = add_data + ['--add-data', src + ';.']
            print('  +  встраиваем файл мода:', os.path.basename(src))

    args = [
        sys.executable, '-m', 'PyInstaller',
        '--onefile', '--noconsole',
        '--name', 'RogueTowerRussian_Installer',
        '--distpath', DIST,
        '--workpath', os.path.join(HERE, 'build'),
        '--specpath', HERE,
    ] + icon_args + add_data + [
        os.path.join(HERE, 'mod_installer.py'),
    ]
    print('Запуск: ', ' '.join(args))
    r = subprocess.run(args)
    if r.returncode != 0:
        print('Ошибка сборки PyInstaller, код', r.returncode)
        return False

    exe = os.path.join(DIST, 'RogueTowerRussian_Installer.exe')
    if not os.path.exists(exe):
        print('EXE не найден:', exe)
        return False

    # 2) Кладём файлы мода рядом с .exe, чтобы установщик их скопировал.
    for f in ['RogueTowerRussian.dll', 'translations.json']:
        src = os.path.join(PLUGINS, f)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(DIST, f))
            print(f'  + {f}')
        else:
            print(f'  ! нет {f} в {PLUGINS}')

    # Исходники (открытость мода)
    for f in ['TranslatorPlugin.cs', 'build_mod32.py', 'changelog.txt']:
        src = os.path.join(GAME, 'mod_translator', f)
        d = os.path.join(GAME, f)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(DIST, f))
            print(f'  + {f}')
        elif os.path.exists(d):
            shutil.copy2(d, os.path.join(DIST, f))
            print(f'  + {f} (из корня)')

    # Встроенный BepInEx — дополнительно кладём рядом с .exe (на случай запуска без onefile).
    if bep_zip and os.path.abspath(bep_zip) != os.path.abspath(os.path.join(DIST, 'bepinex_core.zip')):
        shutil.copy2(bep_zip, os.path.join(DIST, 'bepinex_core.zip'))
        print('  + bepinex_core.zip (автономный BepInEx)')

    print('\nГотово. Установщик:', exe)
    print('Установщик автономный: мод и BepInEx встроены в один .exe.')
    return True

if __name__ == '__main__':
    main()
