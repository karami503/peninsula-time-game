# Rasterise Overture land cover / land / land_use / water into a land-class grid aligned with the height grid,
# fix the coastline from Overture land polygons, carve inland water, and export lake polygons with surface heights.
import sys, os, json, numpy as np, pyarrow.parquet as pq
from PIL import Image, ImageDraw
from shapely import wkb
from shapely.geometry import Polygon, MultiPolygon
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cw_common import *
O = sys.argv[1]; H = np.load(sys.argv[2]).astype(np.float64); out = sys.argv[3]
SEA, WATER, FOREST, GRASS, FARM, ORCHARD, RES, COM, IND, SAND, ROCK, URBAN, WET, GOLF, CEMETERY, CAMPUS, PITCH = range(17)
def polys(geom):
    if geom.geom_type == 'Polygon': return [geom]
    if geom.geom_type == 'MultiPolygon': return list(geom.geoms)
    if geom.geom_type == 'GeometryCollection': return [g for x in geom.geoms for g in polys(x)]
    return []
def pix(ring):
    return [((ORIGIN_LON + 0 if False else (lon - ORIGIN_LON) * SX - X0) / CELL, (NZ - 1) - ((lat - ORIGIN_LAT) * SZ - Z0) / CELL) for lon, lat in ring.coords]
def raster(rows, value_of, img, holes=None):
    # Larger polygons first so features inside their holes are drawn afterwards; holes take the 'holes' value.
    d = ImageDraw.Draw(img); items = []
    for r in rows:
        v = value_of(r)
        if v is None: continue
        for p in polys(wkb.loads(r['geometry'])):
            if len(p.exterior.coords) >= 3: items.append((p.area, p, v))
    items.sort(key=lambda t: -t[0])
    for _, p, v in items:
        d.polygon(pix(p.exterior), fill=v)
        if holes is not None:
            for i in p.interiors: d.polygon(pix(i), fill=holes)
def read(k, cols=('subtype', 'class', 'geometry', 'names')):
    return pq.read_table(f'{O}/{k}.parquet', columns=list(cols)).to_pylist()
# --- land mask (coastline)
mask = Image.new('L', (NX, NZ), 0)
raster([r for r in read('land') if r['subtype'] == 'land'], lambda r: 255, mask, holes=0)
land = np.asarray(mask)[::-1] > 0  # row 0 = southmost (z = Z0)
# --- classes, lowest priority first
cls = Image.new('L', (NX, NZ), URBAN)
COVER = {'forest': FOREST, 'crop': FARM, 'grass': GRASS, 'urban': URBAN, 'barren': ROCK, 'wetland': WET, 'shrub': GRASS, 'mangrove': WET, 'moss': GRASS}
raster(read('land_cover', ('subtype', 'geometry')), lambda r: COVER.get(r['subtype']), cls, holes=URBAN)
LAND = {'forest': FOREST, 'tree': None, 'shrub': GRASS, 'grass': GRASS, 'wetland': WET, 'rock': ROCK, 'sand': SAND}
raster([r for r in read('land') if r['subtype'] != 'land'], lambda r: LAND.get(r['subtype']), cls)
USE = {'agriculture': FARM, 'horticulture': ORCHARD, 'residential': RES, 'park': GRASS, 'managed': GRASS, 'cemetery': CEMETERY, 'education': CAMPUS,
       'golf': GOLF, 'recreation': PITCH, 'developed': None, 'pedestrian': COM, 'construction': ROCK, 'aquaculture': None, 'religious': GRASS,
       'medical': CAMPUS, 'military': IND, 'transportation': IND, 'protected': FOREST, 'resource_extraction': ROCK, 'entertainment': COM, 'campground': GRASS}
def use_of(r):
    if r['subtype'] == 'developed': return {'industrial': IND, 'commercial': COM, 'retail': COM, 'works': IND}.get(r['class'], URBAN)
    if r['subtype'] == 'recreation' and r['class'] not in ('pitch', 'track', 'stadium', 'sports_centre'): return GRASS
    return USE.get(r['subtype'])
lu = read('land_use'); lu.sort(key=lambda r: 1 if r['subtype'] in ('recreation', 'education', 'golf') else 0)
raster(lu, use_of, cls)
C = np.asarray(cls)[::-1].copy()
# --- dense building footprints mark urban fabric where cover says forest/grass/farm
bimg = Image.new('L', (NX, NZ), 0); bd = ImageDraw.Draw(bimg)
for g in pq.read_table(f'{O}/buildings.parquet', columns=['geometry']).column('geometry').to_pylist():
    for p in polys(wkb.loads(g)): bd.polygon(pix(p.exterior), fill=255)
from PIL import ImageFilter
dense = np.asarray(bimg.filter(ImageFilter.BoxBlur(3)))[::-1] > 40
C[dense & np.isin(C, [FOREST, GRASS, FARM, ORCHARD])] = URBAN
# --- the elevation source is a surface model: buildings and street trees add 5-20 m bumps in town. Remove them in built-up
# areas with a morphological opening (drops anything narrower than ~110 m) and smooth, blending back into untouched hills.
from scipy import ndimage
urban = np.isin(C, [RES, COM, IND, URBAN, CAMPUS, PITCH, GRASS, GOLF, CEMETERY, FARM, ORCHARD, SAND])
opened = ndimage.grey_opening(H, size=(7, 7))
flat = ndimage.gaussian_filter(opened, 2.5)
weight = np.clip(ndimage.gaussian_filter(urban.astype(np.float64), 3) * 1.4, 0, 1)
H = H * (1 - weight) + flat * weight
H = ndimage.gaussian_filter(H, 0.8)
# --- coastline fix
C[~land] = SEA
H[~land] = np.minimum(H[~land], -2.5)
H[land] = np.maximum(H[land], 0.8)
# --- inland water: carve and export lakes/reservoirs/rivers with a surface height
lakes = []
wimg = Image.new('L', (NX, NZ), 0); d = ImageDraw.Draw(wimg)
for r in read('water'):
    if r['subtype'] in ('ocean', 'physical', 'human_made', 'spring', 'stream') or r['subtype'] == 'canal' and r['class'] != 'canal': continue
    for p in polys(wkb.loads(r['geometry'])):
        if p.area * SX * SZ < 1500: continue
        m = Image.new('L', (NX, NZ), 0); ImageDraw.Draw(m).polygon(pix(p.exterior), fill=1)
        inside = np.asarray(m)[::-1] > 0
        if inside.sum() < 3: continue
        ring = [((lon - ORIGIN_LON) * SX, (lat - ORIGIN_LAT) * SZ) for lon, lat in p.simplify(0.00003).exterior.coords]
        hs = []
        for x, z in ring:
            i, j = int(round((x - X0) / CELL)), int(round((z - Z0) / CELL))
            if 0 <= i < NX and 0 <= j < NZ: hs.append(H[j, i])
        if not hs: continue
        surface = float(np.percentile(hs, 15))
        if surface < 0.5: continue  # tidal/sea water handled by the sea plane
        H[inside] = np.minimum(H[inside], surface - 1.5); C[inside] = WATER
        nm = (r['names'] or {}).get('primary') if r['names'] else None
        lakes.append({'name': nm or '', 'kind': r['subtype'], 'surface': round(surface, 2), 'ring': [[round(x, 1), round(z, 1)] for x, z in ring]})
json.dump(lakes, open(out + '_lakes.json', 'w'), ensure_ascii=False)
np.save(out + '_height.npy', H.astype(np.float32)); np.save(out + '_class.npy', C)
print('lakes', len(lakes), 'land cells', int(land.sum()), 'classes', np.bincount(C.ravel(), minlength=17).tolist())
# preview
PAL = np.array([[30, 70, 120], [50, 110, 160], [60, 110, 60], [130, 170, 100], [180, 190, 120], [150, 160, 90], [200, 180, 160], [220, 160, 140], [170, 160, 180],
                [230, 220, 170], [150, 150, 140], [190, 190, 190], [100, 140, 120], [110, 190, 110], [140, 160, 130], [210, 200, 150], [120, 180, 120]], np.uint8)
Image.fromarray(PAL[C[::-1]]).resize((1200, int(1200 * NZ / NX))).save(out + '_class.png')
