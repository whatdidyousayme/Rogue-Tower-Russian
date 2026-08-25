# -*- coding: utf-8 -*-
"""Компиляция BepInEx-мода (32-бит) через csc.exe"""
import subprocess
import os
import sys

# Пути - используем 32-битный компилятор
CSC = r'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
# Папка игры: берём из расположения скрипта (при необходимости — переменная окружения ROGUE_TOWER_DIR).
BASE = os.environ.get('ROGUE_TOWER_DIR', os.path.dirname(os.path.abspath(__file__)))

def main():
    print("=== Компиляция 32-битного мода Rogue Tower Russian ===\n")
    
    plugins_dir = os.path.join(BASE, 'BepInEx', 'plugins')
    if not os.path.exists(plugins_dir):
        os.makedirs(plugins_dir)
        print(f"Создана папка: {plugins_dir}")
    
    refs = [
        os.path.join(BASE, 'BepInEx', 'core', 'BepInEx.dll'),
        os.path.join(BASE, 'BepInEx', 'core', '0Harmony.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'UnityEngine.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'UnityEngine.CoreModule.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'Unity.TextMeshPro.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'UnityEngine.UI.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'UnityEngine.UIModule.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'UnityEngine.InputLegacyModule.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'UnityEngine.ScreenCaptureModule.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'UnityEngine.IMGUIModule.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'UnityEngine.TextRenderingModule.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'UnityEngine.TextCoreModule.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'Newtonsoft.Json.dll'),
        os.path.join(BASE, 'Rogue Tower_Data', 'Managed', 'netstandard.dll'),
    ]
    
    missing = [r for r in refs if not os.path.exists(r)]
    if missing:
        print("ОШИБКА: Не найдены файлы:")
        for m in missing:
            print(f"  {m}")
        sys.exit(1)
    
    print("Все зависимости найдены ✓")
    
    cmd = [CSC, '/target:library', '/nologo', f'/out:{os.path.join(plugins_dir, "RogueTowerRussian.dll")}']
    for r in refs:
        cmd.append(f'/reference:{r}')
    cmd.append(os.path.join(BASE, 'mod_translator', 'TranslatorPlugin.cs'))
    
    print("\nКомпиляция (32-бит)...")
    
    result = subprocess.run(cmd, capture_output=True, text=True)
    
    if result.returncode == 0:
        dll_path = os.path.join(plugins_dir, 'RogueTowerRussian.dll')
        if os.path.exists(dll_path):
            size = os.path.getsize(dll_path)
            print(f"✓ Мод скомпилирован: {dll_path} ({size} байт)")
            return True
        else:
            print("✗ DLL не создан!")
            return False
    else:
        print(f"✗ Ошибка компиляции (код {result.returncode}):")
        if result.stdout:
            print("--- STDOUT ---")
            print(result.stdout)
        if result.stderr:
            print("--- STDERR ---")
            print(result.stderr)
        return False

if __name__ == '__main__':
    main()