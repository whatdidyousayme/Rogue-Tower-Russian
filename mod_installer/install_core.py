"""Validated offline installation, also usable without the GUI."""
import hashlib
import configparser
import json
import os
import shutil
import struct
import sys
import tempfile
import zipfile
from datetime import datetime
from pathlib import Path, PurePosixPath

MOD_FILES = ('RogueTowerRussian.dll', 'translations.json')
REQUIRED = ('winhttp.dll', 'doorstop_config.ini', 'BepInEx/core/BepInEx.dll',
            'BepInEx/core/BepInEx.Preloader.dll', 'BepInEx/core/0Harmony.dll')

def pe_machine(data):
    if data[:2] != b'MZ':
        raise ValueError('Файл не является исполняемым файлом Windows.')
    offset = struct.unpack_from('<I', data, 60)[0]
    if data[offset:offset+4] != b'PE\0\0':
        raise ValueError('Некорректный заголовок PE.')
    return struct.unpack_from('<H', data, offset + 4)[0]

def payload_dir():
    if getattr(sys, 'frozen', False):
        return Path(sys._MEIPASS) / 'payload'
    return Path(__file__).resolve().parents[1] / 'build/payload'

def validate_archive(path, machine):
    with zipfile.ZipFile(path) as archive:
        names = set(archive.namelist())
        missing = set(REQUIRED) - names
        if missing:
            raise RuntimeError('Архив BepInEx неполный: ' + ', '.join(sorted(missing)))
        for name in names:
            part = PurePosixPath(name.replace('\\', '/'))
            if part.is_absolute() or '..' in part.parts or ':' in name:
                raise RuntimeError('Недопустимый путь в архиве BepInEx: ' + name)
        if pe_machine(archive.read('winhttp.dll')) != machine:
            raise RuntimeError('Разрядность BepInEx не совпадает с разрядностью игры.')
        if archive.testzip():
            raise RuntimeError('Архив BepInEx повреждён.')

def validate_payload(payload, machine):
    for name in MOD_FILES + ('bepinex_core.zip',):
        if not (payload / name).is_file():
            raise RuntimeError('В установщике отсутствует ' + name + '. Пересоберите установщик.')
    pe_machine((payload / MOD_FILES[0]).read_bytes())
    catalog = json.loads((payload / MOD_FILES[1]).read_text(encoding='utf-8-sig'))
    if not isinstance(catalog, dict) or not catalog or any(not isinstance(k, str) or not isinstance(v, str) or not v for k,v in catalog.items()):
        raise RuntimeError('Словарь переводов повреждён или пуст.')
    validate_archive(payload / 'bepinex_core.zip', machine)

def bepinex_ready(game):
    game = Path(game)
    try:
        config = configparser.ConfigParser()
        config.read(game / 'doorstop_config.ini', encoding='utf-8-sig')
        section = 'General' if config.has_section('General') else 'UnityDoorstop'
        target = config.get(section, 'target_assembly').replace('\\', '/')
        return all((game / p).is_file() for p in REQUIRED) and pe_machine((game / 'winhttp.dll').read_bytes()) == pe_machine((game / 'Rogue Tower.exe').read_bytes()) and config.getboolean(section, 'enabled') and target.lower() == 'bepinex/core/bepinex.preloader.dll'
    except (OSError, ValueError, struct.error, configparser.Error):
        return False

def install(game, payload=None, progress=lambda message: None):
    game = Path(game).resolve()
    payload = Path(payload or payload_dir())
    exe = game / 'Rogue Tower.exe'
    if not exe.is_file() or not (game / 'Rogue Tower_Data/Managed/Assembly-CSharp.dll').is_file():
        raise RuntimeError('Выберите папку установленной игры Rogue Tower.')
    machine = pe_machine(exe.read_bytes())
    validate_payload(payload, machine)  # Fail before changing any files.
    if os.name == 'nt':
        import subprocess
        result = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq Rogue Tower.exe', '/FO', 'CSV', '/NH'], capture_output=True, creationflags=0x08000000)
        if b'rogue tower.exe' in result.stdout.lower():
            raise RuntimeError('Закройте Rogue Tower перед установкой мода.')
    progress('Подготовка файлов и резервной копии...')
    with tempfile.TemporaryDirectory(prefix='rogue-russian-') as tmp:
        stage = Path(tmp)
        if not bepinex_ready(game):
            with zipfile.ZipFile(payload / 'bepinex_core.zip') as archive:
                for member in archive.infolist():
                    # Keep the player's settings. BepInEx generates its own defaults.
                    if member.filename.startswith('BepInEx/config/'):
                        continue
                    archive.extract(member, stage)
        for name in MOD_FILES:
            dest = stage / 'BepInEx/plugins' / name
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(payload / name, dest)
        for source in payload.iterdir():
            if source.suffix in ('.cs', '.py', '.md', '.txt'):
                dest = stage / 'RogueTowerRussian_sources' / source.name
                dest.parent.mkdir(exist_ok=True)
                shutil.copy2(source, dest)
        files = [p for p in stage.rglob('*') if p.is_file()]
        backup = game / 'RogueTowerRussian_backups' / datetime.now().strftime('%Y%m%d-%H%M%S-%f')
        backup.mkdir(parents=True)
        originals = []
        applied = []
        for src in files:
            rel = src.relative_to(stage)
            dest = game / rel
            if dest.exists():
                saved = backup / rel
                saved.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(dest, saved)
                originals.append(str(rel))
        (backup / 'manifest.json').write_text(json.dumps({'originals': originals}, ensure_ascii=False, indent=2), encoding='utf-8')
        try:
            progress('Установка BepInEx и русского перевода...')
            for src in files:
                rel = src.relative_to(stage)
                dest = game / rel
                dest.parent.mkdir(parents=True, exist_ok=True)
                applied.append(rel)
                shutil.copy2(src, dest)
                if hashlib.sha256(src.read_bytes()).digest() != hashlib.sha256(dest.read_bytes()).digest():
                    raise RuntimeError('Проверка копирования не пройдена: ' + str(rel))
            if not bepinex_ready(game):
                raise RuntimeError('BepInEx не прошёл проверку после установки.')
        except Exception:
            for rel in reversed(applied):
                dest, saved = game / rel, backup / rel
                if saved.is_file():
                    shutil.copy2(saved, dest)
                elif dest.is_file():
                    dest.unlink()
            raise
    # A backup DLL inside plugins would itself be loaded by BepInEx.
    legacy = game / 'BepInEx/plugins/_backup'
    if legacy.is_dir():
        shutil.move(str(legacy), str(backup / 'legacy_plugin_backup'))
    progress('Установлено и проверено. Запустите игру через Steam.')
    return backup

def restore_catalog(game, payload=None):
    game = Path(game)
    payload = Path(payload or payload_dir())
    dst = game / 'BepInEx/plugins/translations.json'
    if not dst.is_file():
        raise RuntimeError('Сначала установите мод.')
    backup = game / 'RogueTowerRussian_backups' / datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    backup.mkdir(parents=True)
    shutil.copy2(dst, backup / 'translations.json')
    shutil.copy2(payload / 'translations.json', dst)

def uninstall(game):
    game = Path(game).resolve()
    if not (game / 'Rogue Tower.exe').is_file():
        raise RuntimeError('Выберите папку Rogue Tower.')
    for name in MOD_FILES:
        (game / 'BepInEx/plugins' / name).unlink(missing_ok=True)
    # BepInEx can be shared with other mods and must remain in place.
