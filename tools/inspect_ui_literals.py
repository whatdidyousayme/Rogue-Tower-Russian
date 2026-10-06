import json
import re
from pathlib import Path
root = Path(__file__).resolve().parents[1]
catalog = json.loads((root / 'translations.json').read_text(encoding='utf-8'))
norm = {re.sub(r'\s+', ' ', k).strip().lower() for k in catalog}
methods = (root / 'build/game_il.txt').read_text(encoding='utf-8-sig').split('METHOD ')
missing = {s for m in methods if '::set_text(' in m or '::SetText(' in m
           for s in re.findall(r'ldstr "(.*?)"\n', m, re.S)
           if s.strip() and re.search('[a-zA-Z]', s) and re.sub(r'\s+', ' ', s).strip().lower() not in norm}
print(json.dumps(sorted(missing), ensure_ascii=False, indent=2))
