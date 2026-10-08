# Build the Changwon height grid (int16 decimetres) from terrarium tiles, z13 with z12/z11 fallback.
import sys, os, math, numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cw_common import *
D = sys.argv[1]; out = sys.argv[2]
cache = {}
def tile(z, x, y):
    k = (z, x, y)
    if k not in cache:
        p = os.path.join(D, {13: 'terrain', 12: 'terrain12', 11: 'terrain11'}[z], f'{z}_{x}_{y}.png')
        if os.path.exists(p):
            a = np.asarray(Image.open(p).convert('RGB')).astype(np.float64); cache[k] = a[..., 0] * 256 + a[..., 1] + a[..., 2] / 256 - 32768
        else: cache[k] = None
    return cache[k]
xs = X0 + np.arange(NX) * CELL; zs = Z0 + np.arange(NZ) * CELL
lon = ORIGIN_LON + xs / SX; lat = ORIGIN_LAT + zs / SZ
H = np.zeros((NZ, NX), np.float64); src = np.zeros((NZ, NX), np.int8)
for z in (11, 12, 13):
    n = 2 ** z * 256
    px = (lon + 180) / 360 * n
    py = (1 - np.arcsinh(np.tan(np.radians(lat))) / np.pi) / 2 * n
    PX, PY = np.meshgrid(px, py)
    x0 = np.floor(PX - .5).astype(int); y0 = np.floor(PY - .5).astype(int); fx = PX - .5 - x0; fy = PY - .5 - y0
    def sample(xx, yy):
        out = np.full(xx.shape, np.nan)
        tx, ty = xx // 256, yy // 256
        for (a, b) in set(zip(tx.ravel().tolist(), ty.ravel().tolist())):
            t = tile(z, a, b)
            if t is None: continue
            m = (tx == a) & (ty == b); out[m] = t[yy[m] - b * 256, xx[m] - a * 256]
        return out
    v = (sample(x0, y0) * (1 - fx) * (1 - fy) + sample(x0 + 1, y0) * fx * (1 - fy) + sample(x0, y0 + 1) * (1 - fx) * fy + sample(x0 + 1, y0 + 1) * fx * fy)
    ok = ~np.isnan(v); H[ok] = v[ok]; src[ok] = z
print('grid', NX, NZ, 'cells', NX * NZ, 'missing', int((src == 0).sum()), 'min', H.min(), 'max', H.max())
np.save(out, H.astype(np.float32))
