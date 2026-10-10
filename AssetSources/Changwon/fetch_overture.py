# Extract Overture Maps features in the Changwon bbox to local parquet.
# Uses HTTP range reads with retries (the proxy intermittently answers 404) and parquet row-group bbox statistics.
import io, sys, time, re, urllib.request, urllib.error, concurrent.futures
import pyarrow as pa, pyarrow.parquet as pq, pyarrow.compute as pc
out = sys.argv[1]; which = sys.argv[2:]
HOST = 'https://overturemaps-us-west-2.s3.us-west-2.amazonaws.com'
R = 'release/2026-09-23.1'
PATHS = {'divisions': 'divisions/type=division_area', 'division_points': 'divisions/type=division', 'buildings': 'buildings/type=building', 'building_parts': 'buildings/type=building_part', 'places': 'places/type=place',
         'segments': 'transportation/type=segment', 'connectors': 'transportation/type=connector', 'water': 'base/type=water', 'land_use': 'base/type=land_use',
         'land': 'base/type=land', 'infrastructure': 'base/type=infrastructure', 'land_cover': 'base/type=land_cover'}
X0, X1, Y0, Y1 = 128.30, 128.92, 34.95, 35.45

def get(url, rng=None, tries=12):
    for t in range(tries):
        try:
            req = urllib.request.Request(url, headers={'Range': f'bytes={rng[0]}-{rng[1]}'} if rng else {})
            return urllib.request.urlopen(req, timeout=60).read()
        except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError, ConnectionError) as e:
            err = e; time.sleep(min(8, 0.3 * 2 ** t))
    raise err

def listing(prefix):
    keys, token = [], None
    while True:
        q = f'{HOST}/?list-type=2&prefix={prefix}' + (f'&continuation-token={urllib.parse.quote(token)}' if token else '')
        x = get(q).decode()
        keys += [(k, int(s)) for k, s in re.findall(r'<Key>([^<]+)</Key>.*?<Size>(\d+)</Size>', x)]
        m = re.search(r'<NextContinuationToken>([^<]+)</NextContinuationToken>', x)
        if not m: return [k for k in keys if k[0].endswith('.parquet')]
        token = m.group(1)

class RangeFile(io.RawIOBase):
    def __init__(self, url, size): self.url, self.size, self.pos = url, size, 0
    def seekable(self): return True
    def readable(self): return True
    def tell(self): return self.pos
    def seek(self, off, whence=0):
        self.pos = off if whence == 0 else self.pos + off if whence == 1 else self.size + off; return self.pos
    def readinto(self, b):
        n = min(len(b), self.size - self.pos)
        if n <= 0: return 0
        data = get(self.url, (self.pos, self.pos + n - 1)); b[:len(data)] = data; self.pos += len(data); return len(data)

import urllib.parse
def one(key_size):
    key, size = key_size
    f = RangeFile(f'{HOST}/{key}', size)
    pf = pq.ParquetFile(io.BufferedReader(f, buffer_size=1 << 20))
    md = pf.metadata; names = [md.schema.column(i).path for i in range(md.num_columns)]
    idx = {n: names.index(n) for n in ('bbox.xmin', 'bbox.xmax', 'bbox.ymin', 'bbox.ymax')}
    rgs = []
    for r in range(md.num_row_groups):
        st = {n: md.row_group(r).column(i).statistics for n, i in idx.items()}
        if any(s is None or not s.has_min_max for s in st.values()): rgs.append(r); continue
        if st['bbox.xmin'].min < X1 and st['bbox.xmax'].max > X0 and st['bbox.ymin'].min < Y1 and st['bbox.ymax'].max > Y0: rgs.append(r)
    if not rgs: return None
    cols = [c for c in pf.schema_arrow.names if c != 'sources']
    t = pf.read_row_groups(rgs, columns=cols)
    bb = t.column('bbox')
    m = pc.and_(pc.and_(pc.less(pc.struct_field(bb, 'xmin'), X1), pc.greater(pc.struct_field(bb, 'xmax'), X0)),
                pc.and_(pc.less(pc.struct_field(bb, 'ymin'), Y1), pc.greater(pc.struct_field(bb, 'ymax'), Y0)))
    t = t.filter(m)
    return t if t.num_rows else None

for k in which:
    t0 = time.time(); files = listing(f'{R}/theme={PATHS[k]}/')
    with concurrent.futures.ThreadPoolExecutor(24) as ex: parts = [p for p in ex.map(one, files) if p is not None]
    tbl = pa.concat_tables(parts, promote_options='permissive') if parts else None
    if tbl is not None: pq.write_table(tbl, f'{out}/{k}.parquet')
    print(k, len(files), 'files', tbl.num_rows if tbl is not None else 0, 'rows', round(time.time() - t0), 's', flush=True)
