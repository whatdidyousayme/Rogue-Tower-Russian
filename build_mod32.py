"""Build the plugin from this checkout; never writes into the game directory."""
import argparse
import os
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DEFAULT_GAME = Path(os.environ.get('ROGUE_TOWER_DIR', r'E:\SteamLibrary\steamapps\common\Rogue Tower'))

def build(game=DEFAULT_GAME):
    game = Path(game)
    core = ROOT / 'build/bepinex'
    with zipfile.ZipFile(ROOT / 'bepinex_core.zip') as archive:
        archive.extractall(core)
    managed = game / 'Rogue Tower_Data/Managed'
    refs = [core / 'BepInEx/core' / f for f in ('BepInEx.dll', '0Harmony.dll')]
    refs += [managed / f for f in ('UnityEngine.dll', 'UnityEngine.CoreModule.dll',
        'Unity.TextMeshPro.dll', 'UnityEngine.UI.dll', 'UnityEngine.UIModule.dll',
        'UnityEngine.InputLegacyModule.dll', 'UnityEngine.IMGUIModule.dll',
        'UnityEngine.TextRenderingModule.dll', 'UnityEngine.TextCoreModule.dll',
        'Newtonsoft.Json.dll', 'netstandard.dll')]
    missing = [str(p) for p in refs if not p.is_file()]
    if missing:
        raise RuntimeError('Missing build references:\n' + '\n'.join(missing))
    output = ROOT / 'build/RogueTowerRussian.dll'
    cmd = [r'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe',
           '/nologo', '/target:library', '/out:' + str(output)]
    cmd += ['/reference:' + str(p) for p in refs]
    cmd += [str(ROOT / f) for f in ('TranslatorPlugin.cs', 'TranslationCatalog.cs', 'TextLayout.cs')]
    subprocess.run(cmd, check=True)
    print('Built:', output)
    return output

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--game', type=Path, default=DEFAULT_GAME)
    args = parser.parse_args()
    try:
        build(args.game)
    except Exception as error:
        print(error, file=sys.stderr)
        sys.exit(1)
