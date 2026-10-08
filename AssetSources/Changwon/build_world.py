# Builds the Changwon open-world data used by PeninsulaTime.ChangwonData (Assets/Resources/Changwon/*.bytes|txt).
# Inputs (see README.md in this folder): the height grid, land classes and lakes from build_land.py, Overture Maps
# parquet extracts (segments, buildings, places, infrastructure) and Resources/Geo/ChangwonBuses.txt.
# usage: python3 build_world.py <data dir with cw_* and overture/> <Unity Assets dir>
import sys, os, io, gzip, json, math, struct, hashlib, collections
import numpy as np, pyarrow.parquet as pq
from shapely import wkb
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cw_common import *

D, ASSETS = sys.argv[1], sys.argv[2]
OUT = os.path.join(ASSETS, 'Resources', 'Changwon'); os.makedirs(OUT, exist_ok=True)
H = np.load(f'{D}/cw_height.npy').astype(np.float64)
C = np.load(f'{D}/cw_class.npy')
lakes = json.load(open(f'{D}/cw_lakes.json'))
CX = int(math.ceil((NX - 1) * CELL / CHUNK)); CZ = int(math.ceil((NZ - 1) * CELL / CHUNK))

def bilinear(x, z):
    fx = np.clip((np.asarray(x) - X0) / CELL, 0, NX - 1.001); fz = np.clip((np.asarray(z) - Z0) / CELL, 0, NZ - 1.001)
    i = fx.astype(int); j = fz.astype(int); u = fx - i; v = fz - j
    return H[j, i] * (1 - u) * (1 - v) + H[j, i + 1] * u * (1 - v) + H[j + 1, i] * (1 - u) * v + H[j + 1, i + 1] * u * v
def chunk_of(x, z):
    return min(CX - 1, max(0, int((x - X0) // CHUNK))), min(CZ - 1, max(0, int((z - Z0) // CHUNK)))
def hsh(s, n): return int(hashlib.md5(s.encode()).hexdigest()[:8], 16) % n
def name_of(r):
    # Prefer a Korean name: Overture's primary name is occasionally romanised or in another script (e.g. Greek).
    n = r.get('names') or {}
    hangul = lambda s: any('\uac00' <= c <= '\ud7a3' for c in s or '')
    name = n.get('primary') or ''
    if not hangul(name):
        alt = [v for k, v in (n.get('common') or []) if k == 'ko'] + [x['value'] for x in (n.get('rules') or []) if x.get('language') == 'ko' and x.get('value')]
        if alt: name = alt[0]
        elif any(ord(c) > 0x2000 and not hangul(c) for c in name): name = ''
    return name.replace('|', ' ').replace('\n', ' ').strip()
def utf8(s, limit=120):
    b = s.encode('utf-8')[:limit]
    while True:
        try: b.decode('utf-8'); return b
        except UnicodeDecodeError: b = b[:-1]

# ------------------------------------------------------------------ roads
ROAD_CLASS = {'motorway': 0, 'trunk': 1, 'primary': 2, 'secondary': 3, 'tertiary': 4, 'residential': 5, 'unclassified': 5, 'living_street': 5,
              'unknown': 5, 'service': 6, 'track': 7, 'footway': 8, 'path': 8, 'pedestrian': 8, 'steps': 8, 'cycleway': 8, 'bridleway': 8}
WIDTH = {0: 22, 1: 18, 2: 15, 3: 12, 4: 10, 5: 7, 6: 5, 7: 3.5, 8: 2.5, 9: 4, 10: 3.5}
LANES = {0: 3, 1: 2, 2: 2, 3: 2, 4: 1, 5: 1, 6: 1, 7: 1, 8: 0, 9: 0, 10: 0}
seg = pq.read_table(f'{D}/overture/segments.parquet', columns=['id', 'subtype', 'class', 'names', 'connectors', 'road_flags', 'level_rules', 'width_rules', 'access_restrictions', 'geometry']).to_pylist()
nodes = {}; node_xy = []
def node(cid, x, z):
    if cid not in nodes: nodes[cid] = len(node_xy); node_xy.append((x, z))
    return nodes[cid]
edges = []  # dict: a, b, cls, flags, width, lanes, name, pts [(x,z,kind,level)]
for r in seg:
    if r['subtype'] == 'water': continue
    g = wkb.loads(r['geometry'])
    if g.geom_type != 'LineString' or len(g.coords) < 2: continue
    if r['subtype'] == 'rail': cls = 10 if r['class'] in ('light_rail', 'monorail') else 9
    else: cls = ROAD_CLASS.get(r['class'], 5)
    xy = [to_xz(lon, lat) for lon, lat in g.coords]
    seglen = [0.0]
    for p, q in zip(xy, xy[1:]): seglen.append(seglen[-1] + math.dist(p, q))
    L = seglen[-1]
    if L < 0.5: continue
    def ranges(rules, key):
        out = []
        for f in rules or []:
            vals = f.get('values') if key is None else [f.get('value')]
            b = f.get('between') or [0, 1]
            out.append((vals, b[0], b[1]))
        return out
    flags_r = ranges(r['road_flags'], None); level_r = ranges(r['level_rules'], 'value')
    def kind_at(fr):
        k = 0
        for vals, a, b in flags_r:
            if a - 1e-6 <= fr <= b + 1e-6:
                if 'is_bridge' in vals: k = 1
                elif 'is_tunnel' in vals and k == 0: k = 2
        return k
    def level_at(fr):
        lv = 0
        for vals, a, b in level_r:
            if a - 1e-6 <= fr <= b + 1e-6: lv = vals[0] or 0
        return lv
    # densify cut points: vertices + connectors + flag boundaries
    cuts = {i: seglen[i] / L for i in range(len(xy))}
    pts = [(seglen[i] / L, xy[i]) for i in range(len(xy))]
    extra = [c['at'] for c in (r['connectors'] or [])] + [x for _, a, b in flags_r + level_r for x in (a, b)]
    for fr in extra:
        if any(abs(fr - p[0]) * L < 0.3 for p in pts): continue
        d = fr * L; k = max(i for i in range(len(seglen)) if seglen[i] <= d) if d < L else len(seglen) - 2
        k = min(k, len(xy) - 2); t = (d - seglen[k]) / max(1e-9, seglen[k + 1] - seglen[k])
        pts.append((fr, (xy[k][0] + (xy[k + 1][0] - xy[k][0]) * t, xy[k][1] + (xy[k + 1][1] - xy[k][1]) * t)))
    pts.sort(key=lambda p: p[0])
    # A connector within 0.3 m of an existing vertex uses that vertex's exact fraction, so the split below starts there.
    def snap(fr):
        best = min(pts, key=lambda p: abs(p[0] - fr))
        return best[0] if abs(best[0] - fr) * L < 0.35 else fr
    conns = sorted(((snap(c['at']), c['connector_id']) for c in (r['connectors'] or [])), key=lambda c: c[0])
    if not conns or conns[0][0] > 1e-6: conns.insert(0, (0.0, r['id'] + ':s'))
    if conns[-1][0] < 1 - 1e-6: conns.append((1.0, r['id'] + ':e'))
    flags = 0
    acc = json.dumps(r['access_restrictions'] or [])
    if '"heading": "backward"' in acc or "'heading': 'backward'" in acc or 'backward' in acc and 'denied' in acc: flags |= 1
    if r['class'] in ('steps',): flags |= 32
    width = WIDTH[cls]
    for w in r['width_rules'] or []:
        if w.get('value') and not w.get('between'): width = float(w['value'])
    nm = name_of(r)
    for (fa, ca), (fb, cb) in zip(conns, conns[1:]):
        part = [p for p in pts if fa - 1e-6 <= p[0] <= fb + 1e-6]
        if len(part) < 2: continue
        a = node(ca, *part[0][1]); b = node(cb, *part[-1][1])
        out = []
        for i, (fr, (x, z)) in enumerate(part):
            mid = fr if 0 < i < len(part) - 1 else (fr + (part[1][0] if i == 0 else part[-2][0])) / 2
            out.append([x, z, kind_at(mid), level_at(mid)])
        e = {'a': a, 'b': b, 'cls': cls, 'flags': flags, 'width': width, 'lanes': LANES[cls], 'name': nm, 'pts': out, 'is_link': 'is_link' in json.dumps(r['road_flags'] or [])}
        if e['is_link']: e['flags'] |= 2
        edges.append(e)
print('roads: nodes', len(node_xy), 'edges', len(edges))

# ------------------------------------------------------------------ road heights
N = len(node_xy); ny = np.zeros(N); fixed = np.zeros(N, bool); inc = collections.defaultdict(list)
for ei, e in enumerate(edges):
    inc[e['a']].append((ei, 0)); inc[e['b']].append((ei, -1))
nxz = np.array(node_xy)
ny[:] = bilinear(nxz[:, 0], nxz[:, 1])
for n in range(N):
    kinds = [edges[ei]['pts'][end][2] for ei, end in inc[n]]
    fixed[n] = any(k == 0 for k in kinds)
# harmonic interpolation for bridge/tunnel-internal nodes along chains
nbr = collections.defaultdict(list)
for e in edges:
    L = sum(math.dist(p[:2], q[:2]) for p, q in zip(e['pts'], e['pts'][1:])) + 1e-3
    nbr[e['a']].append((e['b'], 1 / L)); nbr[e['b']].append((e['a'], 1 / L))
free = [n for n in range(N) if not fixed[n]]
for it in range(400):
    for n in free:
        w = sum(x for _, x in nbr[n]); ny[n] = sum(ny[m] * x for m, x in nbr[n]) / w if w else ny[n]
for e in edges:
    P = e['pts']; n = len(P)
    ys = list(bilinear(np.array([p[0] for p in P]), np.array([p[1] for p in P])))
    ys[0], ys[-1] = ny[e['a']], ny[e['b']]
    anchor = [i == 0 or i == n - 1 or P[i][2] == 0 for i in range(n)]
    dist = [0.0]
    for p, q in zip(P, P[1:]): dist.append(dist[-1] + math.dist(p[:2], q[:2]))
    i = 0
    while i < n:
        if anchor[i]: i += 1; continue
        a = i - 1; b = i
        while not anchor[b]: b += 1
        for k in range(a + 1, b):
            t = (dist[k] - dist[a]) / max(1e-6, dist[b] - dist[a]); lin = ys[a] + (ys[b] - ys[a]) * t
            g = bilinear(P[k][0], P[k][1])
            if P[k][2] == 1:  # bridge: clear the ground/water under the middle of the span
                clear = 6.0 * max(1, P[k][3]) if g > 0.5 else 12.0
                ramp = min(1.0, min(dist[k] - dist[a], dist[b] - dist[k]) / 40.0)
                ys[k] = lin + max(0.0, g + clear - lin) * ramp
            elif P[k][2] == 2: ys[k] = min(lin, g - 6.0) if min(dist[k] - dist[a], dist[b] - dist[k]) > 30 else lin
            else: ys[k] = lin
        i = b
    for k in range(n): P[k].append(float(ys[k]))
# smooth ground profiles (window ~ 3 points) without moving node heights
for e in edges:
    P = e['pts']
    if len(P) > 2:
        ys = [p[4] for p in P]
        for k in range(1, len(P) - 1):
            if P[k][2] == 0: P[k][4] = (ys[k - 1] + 2 * ys[k] + ys[k + 1]) / 4

# ------------------------------------------------------------------ tunnel mouths
# Overture often marks a long ground->tunnel segment whose far end is the first tunnel vertex; the open approach in between
# would run buried in the hillside. Move the portal to where the hill first covers the road by COVER metres.
COVER = 8.0
moved = 0
for e in edges:
    P = e['pts']; k = 1
    while k < len(P):
        p, q = P[k - 1], P[k]
        if {p[2], q[2]} == {0, 2}:
            ground_first = p[2] == 0; L = math.dist(p[:2], q[:2]); steps = int(L / 4)
            for s in range(1, steps):
                t = s / steps if ground_first else 1 - s / steps
                x = p[0] + (q[0] - p[0]) * t; z = p[1] + (q[1] - p[1]) * t; y = p[4] + (q[4] - p[4]) * t
                if bilinear(x, z) - y >= COVER:
                    P.insert(k, [x, z, 2, p[3], y]); moved += 1; k += 1
                    break
        k += 1
print('tunnel portals moved to cover', moved)

# ------------------------------------------------------------------ carve terrain under ground roads
accW = np.zeros_like(H); accY = np.zeros_like(H)
for e in edges:
    if e['cls'] >= 9 and e['cls'] != 9: continue
    half = e['width'] / 2 + (3 if e['cls'] < 8 else 1)
    P = e['pts']
    for p, q in zip(P, P[1:]):
        if 1 in (p[2], q[2]) or p[2] == q[2] == 2: continue
        L = math.dist(p[:2], q[:2]); steps = max(1, int(L / 6))
        # open approach of a tunnel: carve up to 8 m short of the portal so the hill still frames the mouth
        t0 = min(0.5, 8 / L) if p[2] == 2 else 0.0; t1 = 1 - min(0.5, 8 / L) if q[2] == 2 else 1.0
        for s in range(steps + 1):
            t = t0 + (t1 - t0) * s / steps; x = p[0] + (q[0] - p[0]) * t; z = p[1] + (q[1] - p[1]) * t; y = p[4] + (q[4] - p[4]) * t
            r = int(math.ceil((half + CELL) / CELL)); ci = int(round((x - X0) / CELL)); cj = int(round((z - Z0) / CELL))
            i0, i1, j0, j1 = max(0, ci - r), min(NX - 1, ci + r), max(0, cj - r), min(NZ - 1, cj + r)
            if i0 > i1 or j0 > j1: continue
            gi, gj = np.meshgrid(np.arange(i0, i1 + 1), np.arange(j0, j1 + 1))
            d = np.hypot(X0 + gi * CELL - x, Z0 + gj * CELL - z)
            w = np.clip(1 - (d - half) / CELL, 0, 1) ** 2
            accW[j0:j1 + 1, i0:i1 + 1] += w; accY[j0:j1 + 1, i0:i1 + 1] += w * y
m = accW > 0
target = np.where(m, accY / np.maximum(accW, 1e-9), H)
blend = np.clip(accW, 0, 1)
land = C != 0
H = np.where(m & land, H * (1 - blend) + target * blend, H)
print('carved cells', int(m.sum()))

# ------------------------------------------------------------------ write roads
def chunk_index(items_cells):
    lists = [[] for _ in range(CX * CZ)]
    for idx, cells in items_cells:
        for c in cells: lists[c].append(idx)
    return lists
buf = io.BytesIO(); w = buf.write
w(b'CWR1'); w(struct.pack('<i', N))
for x, z in node_xy: w(struct.pack('<ff', x, z))
w(struct.pack('<i', len(edges)))
cells = []
for ei, e in enumerate(edges):
    nb = utf8(e['name'])
    w(struct.pack('<iiBBBBB', e['a'], e['b'], e['cls'], e['flags'], int(min(255, e['width'] * 4)), e['lanes'], len(nb))); w(nb)
    w(struct.pack('<H', len(e['pts'])))
    cs = set()
    for x, z, k, lv, y in e['pts']:
        w(struct.pack('<fffB', x, y, z, k)); cs.add(chunk_of(x, z))
    cells.append((ei, [cz * CX + cx for cx, cz in cs]))
lists = chunk_index(cells)
w(struct.pack('<iiff', CX, CZ, X0, Z0)); w(struct.pack('<f', CHUNK))
for l in lists:
    w(struct.pack('<i', len(l))); w(struct.pack(f'<{len(l)}i', *l))
open(f'{OUT}/cw_roads.bytes', 'wb').write(gzip.compress(buf.getvalue(), 9))

# ------------------------------------------------------------------ terrain
hd = np.round(H * 10).astype(np.int32)
delta = np.diff(np.concatenate([np.zeros((NZ, 1), np.int32), hd], axis=1), axis=1)
assert np.abs(delta).max() < 32767
buf = io.BytesIO(); buf.write(b'CWT1'); buf.write(struct.pack('<iifff', NX, NZ, X0, Z0, CELL))
buf.write(delta.astype('<i2').tobytes()); buf.write(C.astype(np.uint8).tobytes())
open(f'{OUT}/cw_terrain.bytes', 'wb').write(gzip.compress(buf.getvalue(), 9))

# ------------------------------------------------------------------ buildings
KIND = {'house': 0, 'detached': 0, 'residential': 8, 'apartments': 1, 'dormitory': 1, 'commercial': 2, 'retail': 2, 'office': 2, 'industrial': 3, 'warehouse': 3,
        'factory': 3, 'school': 4, 'university': 4, 'college': 4, 'kindergarten': 4, 'public': 5, 'civic': 5, 'government': 5, 'library': 5, 'post_office': 5,
        'church': 6, 'temple': 6, 'religious': 6, 'shed': 7, 'greenhouse': 7, 'roof': 7, 'shelter': 7, 'hospital': 9, 'hotel': 10, 'train_station': 11, 'transportation': 11}
SUB = {'residential': 8, 'outbuilding': 7, 'industrial': 3, 'commercial': 2, 'education': 4, 'civic': 5, 'religious': 6, 'agricultural': 7, 'medical': 9,
       'entertainment': 2, 'transportation': 11, 'service': 2}
bl = pq.read_table(f'{D}/overture/buildings.parquet', columns=['id', 'subtype', 'class', 'height', 'num_floors', 'names', 'geometry']).to_pylist()
recs = collections.defaultdict(list); nb = 0; skipped = 0
for r in bl:
    g = wkb.loads(r['geometry'])
    polys = [g] if g.geom_type == 'Polygon' else list(g.geoms) if g.geom_type == 'MultiPolygon' else []
    for p in polys:
        ring = [to_xz(lon, lat) for lon, lat in p.exterior.coords][:-1]
        if len(ring) < 3: continue
        if len(ring) > 40:
            ring = [to_xz(lon, lat) for lon, lat in p.simplify(0.000006).exterior.coords][:-1]
            if len(ring) < 3 or len(ring) > 250: skipped += 1; continue
        xs = [q[0] for q in ring]; zs = [q[1] for q in ring]
        area = abs(sum(xs[i] * zs[(i + 1) % len(xs)] - xs[(i + 1) % len(xs)] * zs[i] for i in range(len(xs)))) / 2
        if area < 12: continue
        cxm, czm = sum(xs) / len(xs), sum(zs) / len(zs)
        if not (X0 < cxm < X0 + (NX - 1) * CELL and Z0 < czm < Z0 + (NZ - 1) * CELL): continue
        kind = KIND.get(r['class'], SUB.get(r['subtype'], -1))
        zone = int(C[int(round((czm - Z0) / CELL)), int(round((cxm - X0) / CELL))])
        mrr = p.minimum_rotated_rectangle
        try:
            mc = list(mrr.exterior.coords); s1 = math.dist(*[(to_xz(*mc[0])), to_xz(*mc[1])]); s2 = math.dist(to_xz(*mc[1]), to_xz(*mc[2]))
            aspect = max(s1, s2) / max(1, min(s1, s2))
        except Exception: aspect = 1
        key = r['id']; floors = r['num_floors'] or 0
        if kind < 0:
            if zone == 6 and area > 450 and aspect > 2.0: kind = 1
            elif zone == 6 and area > 250: kind = 8
            elif zone == 7: kind = 2
            elif zone == 8: kind = 3
            elif zone == 15: kind = 4
            elif area < 160: kind = 0
            elif area < 500: kind = 8 if zone in (6, 11) else 2
            else: kind = 2 if zone in (11, 7) else 3
        if kind == 1 and area < 250: kind = 8
        if r['height']: height = float(r['height'])
        elif floors: height = floors * 3.0 + (1.5 if kind == 2 else 0)
        else:
            if kind == 0: floors = 1 + hsh(key, 2)
            elif kind == 1: floors = 15 + hsh(key, 11)
            elif kind == 8: floors = 3 + hsh(key, 3)
            elif kind == 2: floors = 3 + hsh(key, 6 if zone == 7 else 4) + (4 if area > 2500 else 0)
            elif kind == 3: floors = 0
            elif kind == 4: floors = 4 + hsh(key, 2)
            elif kind == 9: floors = 6 + hsh(key, 6)
            elif kind == 10: floors = 8 + hsh(key, 10)
            else: floors = 1 + hsh(key, 2)
            height = floors * 3.0 if floors else (9 + hsh(key, 7))
            if kind == 7: height = 3.2; floors = 1
        height = max(2.6, min(height, 320))
        base = float(min(bilinear(np.array(xs), np.array(zs)).min(), bilinear(cxm, czm)))
        ccx, ccz = chunk_of(cxm, czm); ox = X0 + (ccx + .5) * CHUNK; oz = Z0 + (ccz + .5) * CHUNK
        q = [(int(round((x - ox) * 20)), int(round((z - oz) * 20))) for x, z in ring]
        if any(abs(v) > 32000 for t in q for v in t): skipped += 1; continue
        recs[ccz * CX + ccx].append((kind, min(255, floors or int(round(height / 3))), int(round(height * 10)), int(round(base * 10)), utf8(name_of(r), 90), q))
        nb += 1
buf = io.BytesIO(); buf.write(b'CWB1'); buf.write(struct.pack('<iifff', CX, CZ, X0, Z0, CHUNK))
blob = io.BytesIO(); offs = []
for c in range(CX * CZ):
    offs.append(blob.tell()); blob.write(struct.pack('<i', len(recs[c])))
    for kind, fl, h, b, nm, q in recs[c]:
        blob.write(struct.pack('<BBHhBB', kind, fl, h, b, len(nm), len(q))); blob.write(nm)
        blob.write(struct.pack(f'<{2 * len(q)}h', *[v for t in q for v in t]))
offs.append(blob.tell())
buf.write(struct.pack(f'<{len(offs)}i', *offs)); buf.write(blob.getvalue())
open(f'{OUT}/cw_buildings.bytes', 'wb').write(gzip.compress(buf.getvalue(), 9))
print('buildings', nb, 'skipped', skipped)

# ------------------------------------------------------------------ places, lakes, infrastructure
CAT = [('convenience_store', 'store'), ('food_and_beverage_store', 'store'), ('supermarket', 'store'), ('warehouse_club_store', 'mart'), ('shopping_mall', 'mall'),
       ('department_store', 'mall'), ('coffee_shop', 'cafe'), ('cafe', 'cafe'), ('restaurant', 'food'), ('casual_eatery', 'food'), ('fast_food_restaurant', 'food'),
       ('bar', 'bar'), ('gas_station', 'fuel'), ('hospital', 'hospital'), ('pharmacy', 'pharmacy'), ('hotel', 'hotel'), ('lodging', 'hotel'),
       ('elementary_school', 'school'), ('middle_school', 'school'), ('high_school', 'school'), ('college_university', 'campus'), ('government_office', 'gov'),
       ('police', 'police'), ('fire_station', 'fire'), ('park', 'park'), ('museum', 'museum'), ('historic_site', 'historic'), ('mountain', 'mountain'),
       ('christian_place_of_worship', 'church'), ('buddhist_place_of_worship', 'temple'), ('bank_or_credit_union', 'bank'), ('gym', 'gym'),
       ('sport_or_fitness_facility', 'sport'), ('stadium', 'sport'), ('library', 'library'), ('auto_dealer', 'auto'), ('automotive_service', 'auto'),
       ('music_venue', 'venue'), ('golf_course', 'golf'), ('beach', 'beach'), ('market', 'market'), ('fashion_and_apparel_store', 'shop'), ('electronics_store', 'shop'),
       ('flowers_and_gifts_store', 'shop'), ('sporting_goods_store', 'shop'), ('personal_care_and_beauty_store', 'shop'), ('hardware_home_and_garden_store', 'shop'),
       ('train_station', 'station'), ('bus_station', 'station'), ('ferry_terminal', 'ferry'), ('movie_theater', 'cinema'), ('karaoke', 'karaoke'), ('pc_bang', 'pcbang')]
CATD = dict(CAT)
pl = pq.read_table(f'{D}/overture/places.parquet', columns=['names', 'basic_category', 'taxonomy', 'confidence', 'geometry']).to_pylist()
lines = ['# x|z|kind|name — Overture Maps places (CDLA Permissive 2.0 / ODbL sources), retrieved 2026-10-08']
for r in pl:
    nm = name_of(r)
    if not nm or r['confidence'] < 0.45: continue
    tax = r['taxonomy'] or {}; chain = [r['basic_category']] + list(reversed(tax.get('hierarchy') or []))
    kind = next((CATD[c] for c in chain if c in CATD), None)
    if kind is None: continue
    g = wkb.loads(r['geometry']); x, z = to_xz(g.x, g.y)
    if not (X0 < x < X0 + (NX - 1) * CELL and Z0 < z < Z0 + (NZ - 1) * CELL): continue
    lines.append(f'{x:.1f}|{z:.1f}|{kind}|{nm}')
inf = pq.read_table(f'{D}/overture/infrastructure.parquet', columns=['subtype', 'class', 'names', 'geometry']).to_pylist()
for r in inf:
    if r['class'] not in ('bus_stop', 'traffic_signals', 'street_lamp', 'viewpoint', 'toilets', 'bench', 'speed_camera'): continue
    g = wkb.loads(r['geometry'])
    if g.geom_type != 'Point': continue
    x, z = to_xz(g.x, g.y)
    if X0 < x < X0 + (NX - 1) * CELL and Z0 < z < Z0 + (NZ - 1) * CELL: lines.append(f'{x:.1f}|{z:.1f}|i:{r["class"]}|{name_of(r)}')
open(f'{OUT}/cw_places.txt', 'w').write('\n'.join(lines) + '\n')
out = ['# name|kind|surface y|x,z;x,z… — Overture Maps base water (ODbL)']
for l in lakes:
    if len(l['ring']) >= 3: out.append(f"{l['name']}|{l['kind']}|{l['surface']}|" + ';'.join(f'{x},{z}' for x, z in l['ring']))
open(f'{OUT}/cw_lakes.txt', 'w').write('\n'.join(out) + '\n')
print('places', len(lines) - 1)

# ------------------------------------------------------------------ city buses from the Changwon BIS import (snapped heights)
road_pts = np.array([(p[0], p[1], p[4]) for e in edges if e['cls'] < 8 for p in e['pts']])
from scipy.spatial import cKDTree as KD  # noqa: E402
tree = KD(road_pts[:, :2])
bus = open(os.path.join(ASSETS, 'Resources', 'Geo', 'ChangwonBuses.txt')).read().splitlines()
stops = {}; routes = collections.OrderedDict()
for row in bus:
    f = row.split('|')
    if f[0] == 'S': stops[f[1]] = (f[2], float(f[3]), float(f[4]))
    elif f[0] == 'L': routes[f[1]] = {'name': f[4], 'title': f[3], 'color': f[5]}
    elif f[0] == 'P': routes[f[1]]['stops'] = f[2].split()
    elif f[0] == 'G': routes[f[1]]['shape'] = [tuple(map(float, p.split(','))) for p in f[2].split(';')]
    elif f[0] == 'J': routes[f[1]]['idx'] = list(map(int, f[2].split()))
out = ['# id|number|color|title then shape x,y,z;… then stop index:name;… — Changwon BIS (bus.changwon.go.kr) 2026-10-08, heights from the road network']
for rid, r in routes.items():
    if 'shape' not in r: continue
    xyz = []
    for lon, lat in r['shape']:
        x, z = to_xz(lon, lat); d, k = tree.query((x, z)); y = road_pts[k, 2] if d < 20 else float(bilinear(x, z))
        xyz.append(f'{x:.1f},{y:.1f},{z:.1f}')
    st = ';'.join(f'{i}:{stops[s][0]}' for i, s in zip(r['idx'], r['stops']))
    out.append(f"{rid}|{r['name']}|{r['color']}|{r['title']}|" + ';'.join(xyz) + '|' + st)
open(f'{OUT}/cw_buses.bytes', 'wb').write(gzip.compress(('\n'.join(out) + '\n').encode(), 9))
print('bus routes', len(out) - 1)

# ------------------------------------------------------------------ overview map texture (north up)
MW = 4096; MH = int(round(MW * NZ / NX / 4)) * 4
PAL = {0: (52, 92, 128), 1: (70, 120, 160), 2: (74, 104, 66), 3: (112, 146, 92), 4: (150, 160, 104), 5: (130, 150, 90), 6: (168, 162, 150), 7: (176, 156, 146),
       8: (150, 146, 160), 9: (206, 196, 150), 10: (128, 126, 118), 11: (150, 150, 146), 12: (96, 130, 112), 13: (110, 170, 100), 14: (120, 140, 110), 15: (166, 160, 136), 16: (110, 160, 100)}
lut = np.array([PAL[i] for i in range(17)], np.uint8)
gy, gx = np.gradient(H); shade = np.clip(1 + (gx - gy) * 0.035, 0.7, 1.25)
img = (lut[C] * shade[..., None]).clip(0, 255).astype(np.uint8)[::-1]
mimg = Image.fromarray(img).resize((MW, MH), Image.BILINEAR); dr = ImageDraw.Draw(mimg)
sx = MW / ((NX - 1) * CELL); sz = MH / ((NZ - 1) * CELL)
def mp(x, z): return ((x - X0) * sx, MH - (z - Z0) * sz)
for c, lst in recs.items():
    ccx, ccz = c % CX, c // CX; ox = X0 + (ccx + .5) * CHUNK; oz = Z0 + (ccz + .5) * CHUNK
    for kind, fl, h, b, nm, q in lst:
        pts = [mp(ox + u / 20, oz + v / 20) for u, v in q]
        if len(pts) >= 3: dr.polygon(pts, fill=(118, 116, 112) if kind != 3 else (126, 120, 132))
ORDER = sorted(range(len(edges)), key=lambda i: -edges[i]['cls'])
for i in ORDER:
    e = edges[i]
    if e['cls'] == 8: col, wd = (190, 186, 170), 1
    elif e['cls'] >= 9: col, wd = (70, 70, 76), 2
    elif e['cls'] <= 1: col, wd = (236, 190, 92), 5
    elif e['cls'] <= 3: col, wd = (244, 230, 170), 4
    else: col, wd = (236, 236, 228), 2 if e['cls'] <= 5 else 1
    pts = [mp(p[0], p[1]) for p in e['pts']]
    dr.line(pts, fill=col, width=wd)
mimg.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).save(f'{OUT}/cw_map.png', optimize=True)
json.dump({'mapWidth': MW, 'mapHeight': MH, 'x0': X0, 'z0': Z0, 'x1': X0 + (NX - 1) * CELL, 'z1': Z0 + (NZ - 1) * CELL}, open(f'{D}/map_meta.json', 'w'))
for f in sorted(os.listdir(OUT)):
    if not f.endswith('.meta'): print(f, os.path.getsize(f'{OUT}/{f}'))
