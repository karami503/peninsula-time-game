# Area names for the open-world HUD: the five 구 outlines and the 동·읍·면·리 points (Overture Maps divisions, ODbL).
# usage: python3 build_areas.py <data dir with overture/> <Unity Assets dir>
import sys, os, pyarrow.parquet as pq
from shapely import wkb
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cw_common import *
D, ASSETS = sys.argv[1], sys.argv[2]
out = ['# G|구|x,z;… outline · P|kind|name|x|z — Overture Maps divisions (ODbL), retrieved 2026-10-08']
GU = ('의창구', '성산구', '마산합포구', '마산회원구', '진해구')
for r in pq.read_table(f'{D}/overture/divisions.parquet').to_pylist():
    name = (r['names'] or {}).get('primary')
    if name not in GU: continue
    g = wkb.loads(r['geometry']); polys = [g] if g.geom_type == 'Polygon' else list(g.geoms)
    for p in polys:
        p = p.simplify(0.0004)
        if p.area * SX * SZ < 2e5: continue
        out.append(f'G|{name}|' + ';'.join(f'{x:.0f},{z:.0f}' for x, z in (to_xz(*c) for c in p.exterior.coords)))
for r in pq.read_table(f'{D}/overture/division_points.parquet').to_pylist():
    if r['subtype'] not in ('neighborhood', 'microhood', 'macrohood', 'locality', 'localadmin'): continue
    name = (r['names'] or {}).get('primary') or ''
    g = wkb.loads(r['geometry']); x, z = to_xz(g.x, g.y)
    if not name or name in GU or not (X0 < x < X0 + (NX - 1) * CELL and Z0 < z < Z0 + (NZ - 1) * CELL): continue
    out.append(f"P|{r['subtype']}|{name}|{x:.0f}|{z:.0f}")
open(os.path.join(ASSETS, 'Resources', 'Changwon', 'cw_areas.txt'), 'w').write('\n'.join(out) + '\n')
print('areas', len(out) - 1)
