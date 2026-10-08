# Curated Changwon landmarks (web research, 2026-10-08; sources per entry in landmarks_research.json) for stamps, map markers and models.
# usage: python3 build_landmarks.py <Unity Assets dir>
import sys, os, json, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cw_common import *
data = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'landmarks_research.json')))
out = ['# id|name|gu|x|z|kind|height|description|activity — see AssetSources/Changwon/landmarks_research.json for sources']
for i, l in enumerate(data['landmarks']):
    name = re.sub(r'\s*\([A-Za-z][^)]*\)\s*$', '', l['name']).strip()
    x, z = to_xz(l['lon'], l['lat'])
    clean = lambda s: s.replace('|', '/').replace('\n', ' ').strip()
    out.append(f"lm{i:02d}|{clean(name)}|{clean(l['gu'])}|{x:.1f}|{z:.1f}|{l['kind']}|{l['heightMeters']}|{clean(l['description'])[:220]}|{clean(l['activity'])[:200]}")
open(os.path.join(sys.argv[1], 'Resources', 'Changwon', 'cw_landmarks.txt'), 'w').write('\n'.join(out) + '\n')
print('landmarks', len(out) - 1)
