# Download AWS terrarium elevation tiles covering Changwon at zoom 13.
import math, sys, os, urllib.request, concurrent.futures
out = sys.argv[1]; z = int(sys.argv[2]) if len(sys.argv) > 2 else 13
lat0, lat1, lon0, lon1 = 34.95, 35.45, 128.30, 128.92
def tile(lat, lon):
    n = 2 ** z; x = int((lon + 180) / 360 * n)
    y = int((1 - math.asinh(math.tan(math.radians(lat))) / math.pi) / 2 * n); return x, y
x0, y1 = tile(lat0, lon0); x1, y0 = tile(lat1, lon1)
jobs = [(x, y) for x in range(x0, x1 + 1) for y in range(y0, y1 + 1)]
print('tiles', len(jobs), 'x', x0, x1, 'y', y0, y1)
def get(xy):
    x, y = xy; p = os.path.join(out, f'{z}_{x}_{y}.png')
    if os.path.exists(p) and os.path.getsize(p) > 0: return 0
    for attempt in range(4):
        try:
            data = urllib.request.urlopen(f'https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png', timeout=30).read()
            open(p, 'wb').write(data); return len(data)
        except Exception as e: err = e
    print('fail', xy, err); return -1
with concurrent.futures.ThreadPoolExecutor(16) as ex: sizes = list(ex.map(get, jobs))
print('done', sum(s for s in sizes if s > 0), 'bytes', sum(1 for s in sizes if s < 0), 'failed')
