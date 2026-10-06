"""Extract text from the installed game's assemblies and Unity serialized assets."""
import base64
import json
import re
import subprocess
import struct
from pathlib import Path
import UnityPy
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from build_mod32 import DEFAULT_GAME
GAME = DEFAULT_GAME
BUILD = ROOT / 'build'

def main():
    BUILD.mkdir(exist_ok=True)
    cecil = BUILD / 'bepinex/BepInEx/core/Mono.Cecil.dll'
    subprocess.run([r'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe', '/nologo',
                    '/out:' + str(cecil.parent / 'DumpStrings.exe'), '/reference:' + str(cecil),
                    str(ROOT / 'tools/DumpStrings.cs')], check=True)
    encoded = BUILD / 'assembly_strings.txt'
    subprocess.run([str(cecil.parent / 'DumpStrings.exe'),
                    str(GAME / 'Rogue Tower_Data/Managed/Assembly-CSharp.dll'), str(encoded)], check=True)
    texts = {base64.b64decode(x).decode('utf-8') for x in encoded.read_text(encoding='utf-8-sig').splitlines()}
    asset_texts = set()
    def visit(value):
        if isinstance(value, dict):
            for k, v in value.items():
                if k in ('m_Text', 'm_text') and isinstance(v, str) and v:
                    asset_texts.add(v)
                elif isinstance(v, (dict, list)):
                    visit(v)
        elif isinstance(value, list):
            for v in value:
                visit(v)
    for path in (GAME / 'Rogue Tower_Data').iterdir():
        if path.name.startswith(('level', 'sharedassets', 'globalgamemanagers')) and path.suffix not in ('.resS', '.resource'):
            env = UnityPy.load(str(path))
            for obj in env.objects:
                if obj.type.name == 'MonoBehaviour':
                    try:
                        visit(obj.read_typetree())
                    except (ValueError, TypeError):
                        # This player strips type trees. Unity strings are UTF-8 with
                        # an aligned int32 length; inspect serialized MonoBehaviours.
                        raw = obj.get_raw_data()
                        for offset in range(28, len(raw) - 4, 4):
                            length = struct.unpack_from('<i', raw, offset)[0]
                            if not 2 <= length <= min(12000, len(raw) - offset - 4):
                                continue
                            try:
                                value = raw[offset+4:offset+4+length].decode('utf-8')
                            except UnicodeDecodeError:
                                continue
                            if all(c.isprintable() or c in '\n\r\t' for c in value) and re.search('[A-Za-z]', value):
                                asset_texts.add(value)
    texts.update(asset_texts)
    texts = sorted(s for s in texts if re.search('[A-Za-z]', s))
    (BUILD / 'game_texts.json').write_text(json.dumps(texts, ensure_ascii=False, indent=2), encoding='utf-8')
    translations = json.loads((ROOT / 'translations.json').read_text(encoding='utf-8-sig'))
    keys = {re.sub(r'\s+', ' ', k).strip().lower() for k in translations}
    missing = [s for s in texts if re.sub(r'\s+', ' ', s).strip().lower() not in keys]
    (BUILD / 'missing_texts.json').write_text(json.dumps(missing, ensure_ascii=False, indent=2), encoding='utf-8')
    print('Game texts:', len(texts), 'Asset texts:', len(asset_texts), 'Without exact dictionary entry:', len(missing))

if __name__ == '__main__':
    main()
