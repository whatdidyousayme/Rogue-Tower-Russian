"""Build a self-contained offline installer from the project, not an installed mod."""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(ROOT))
from build_mod32 import build, DEFAULT_GAME
from install_core import validate_archive

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game', type=Path, default=DEFAULT_GAME)
    args = parser.parse_args()
    dll = build(args.game)
    archive = ROOT / 'bepinex_core.zip'
    validate_archive(archive, 0x14c)
    payload = ROOT / 'build/payload'
    payload.mkdir(parents=True, exist_ok=True)
    files = [dll, archive] + [ROOT / f for f in ('translations.json', 'TranslatorPlugin.cs',
             'TranslationCatalog.cs', 'TextLayout.cs', 'build_mod32.py', 'README.md', 'changelog.txt', 'icon.ico')]
    for src in files:
        if not src.is_file():
            raise FileNotFoundError(src)
        shutil.copy2(src, payload / src.name)
    subprocess.run([sys.executable, '-m', 'PyInstaller', '--clean', '--noconfirm',
        '--onefile', '--noconsole', '--name', 'RogueTowerRussian_Installer',
        '--icon', str(ROOT / 'icon.ico'), '--add-data', str(payload) + ';payload',
        '--distpath', str(HERE / 'dist'), '--workpath', str(HERE / 'build'),
        '--specpath', str(HERE), str(HERE / 'mod_installer.py')], check=True)
    shutil.copy2(HERE / 'INSTALLATION.txt', HERE / 'dist/README.txt')
    print('Installer:', HERE / 'dist/RogueTowerRussian_Installer.exe')

if __name__ == '__main__':
    main()
