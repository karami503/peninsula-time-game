#!/usr/bin/env python3
"""Build only real OSM surroundings of every built-in visitable rail station.
One cached country extract, no per-station API requests. See STATION_AREAS_097.md.
Usage: .venv/bin/python build_station_areas.py [--download] [--radius 450]
Dependencies: requirements.txt. Source and build receipts remain beside the script.
"""
import argparse,collections,datetime,gzip,hashlib,json,math,pathlib,re,subprocess,time
import osmium
import numpy as np
import mapbox_earcut
from shapely.geometry import Point,LineString,Polygon
from shapely.affinity import scale
from shapely.strtree import STRtree

HERE=pathlib.Path(__file__).resolve().parent
ROOT=HERE.parent.parent
GEO=ROOT/'PeninsulaTimeUnity/Assets/Resources/Geo'
OUT=GEO/'StationAreas'
CACHE=HERE/'cache'
URL='https://download.geofabrik.de/asia/south-korea-latest.osm.pbf'
SOURCE=CACHE/'south-korea-latest.osm.pbf'
TILE=.01

def network():
    stations={}; lines={}; line_stops={}; grades={}; approx=set()
    sources={}
    for name in ['TransitNetwork','TransitExpansion','ChangwonBuses','StationGrades']:
        path=GEO/(name+'.txt')
        if not path.exists():continue
        raw=path.read_bytes();sources[name]=hashlib.sha256(raw).hexdigest()
        for row in raw.decode('utf8').splitlines():
            f=row.split('|')
            if f[0]=='S' and len(f)>=5:
                stations.setdefault(f[1],dict(id=f[1],name=f[2],lon=float(f[3]),lat=float(f[4])))
            elif f[0]=='L':lines[f[1]]=dict(id=f[1],kind=f[2],name=f[3],planned=False)
            elif f[0]=='Q' and f[1] in lines:lines[f[1]]['planned']=f[2]=='planned'
            elif f[0]=='P':line_stops[f[1]]=f[2].split()
            elif f[0]=='H':grades[f[1]]=f[2]
            elif f[0]=='E' and f[2]=='approximate':approx.add(f[1])
    serving=collections.defaultdict(list)
    for ident,ids in line_stops.items():
        line=lines.get(ident)
        existing=[s for s in ids if s in stations]
        if line and line['kind'] not in ['bus','brt'] and len(existing)>=2:
            for s in set(existing):serving[s].append(line)
    result=[]
    for sid in sorted(serving):
        s=stations[sid];s.update(grade=grades.get(sid,'unknown'),approximate=sid in approx,
            plannedOnly=all(l['planned'] for l in serving[sid]),lines=[l['id'] for l in serving[sid]],tiles=[],roads=0,buildings=0,entrances=0,platforms=0,rails=0)
        result.append(s)
    return result,sources

def number(value):
    m=re.match(r'^\s*([0-9]+(?:\.[0-9]+)?)\s*(m|metres|meters)?\s*$',value or '')
    return float(m[1]) if m else None

def signed_number(value,default=0):
    try:return float(value)
    except (ValueError,TypeError):return default

def safe_tag(tags,key,default=''):return tags.get(key,default)

def dimensions(tags,kind):
    h=number(tags.get('height')); mode='height'
    if h is None:
        levels=number(tags.get('building:levels'))
        h=levels*3 if levels is not None else 6
        mode='levels_estimate' if levels is not None else 'default_estimate'
    width=number(tags.get('width')); wm='width'
    if width is None:
        highway=tags.get('highway','');lanes=number(tags.get('lanes'))
        width=lanes*3.2 if lanes else {'motorway':18,'trunk':14,'primary':12,'secondary':10,'tertiary':8,'residential':6,'service':4,'pedestrian':5,'footway':2,'path':1.8,'cycleway':2.5,'steps':2.5}.get(highway,6)
        if kind=='p':width=3
        if kind=='rail':width=1.435
        wm='lanes_estimate' if lanes else 'default_estimate'
    underground=tags.get('tunnel') not in [None,'no','false'] or tags.get('location')=='underground' or signed_number(tags.get('layer'))<0 or signed_number(tags.get('level'))<0
    return dict(h=round(min(400,max(1,h)),2),base=number(tags.get('min_height')) or 0,w=round(min(50,max(.6,width)),2),heightSource=mode,widthSource=wm,underground=underground,layer=int(signed_number(tags.get('layer'))),bridge=tags.get('bridge') not in [None,'no','false'])

class Extract(osmium.SimpleHandler):
    def __init__(self,stations,radius):
        super().__init__();self.stations=stations;self.radius=radius
        self.buffers=[scale(Point(s['lon'],s['lat']).buffer(1,resolution=24),radius/(111320*math.cos(math.radians(s['lat']))),radius/111320,origin=(s['lon'],s['lat'])) for s in stations]
        self.tree=STRtree(self.buffers);self.tiles={};self.seen=set();self.errors=collections.Counter();self.counts=collections.Counter();self.processed=collections.Counter();self.last=time.time()
    def match(self,geometry):
        return [int(i) for i in self.tree.query(geometry,predicate='intersects')]
    def progress(self):
        if time.time()-self.last>20:
            self.last=time.time();print('scan',dict(self.processed),'kept',dict(self.counts),flush=True)
    def add(self,ident,kind,tags,geometry,rings=None):
        if ident in self.seen:return
        matches=self.match(geometry)
        if not matches:return
        # Keep original polygons, including courtyards. Widths/heights remain tagged vs estimated.
        cx,cy=geometry.centroid.coords[0];tx=math.floor(cx/TILE);ty=math.floor(cy/TILE);key=f'{tx}_{ty}'
        tile=self.tiles.setdefault(key,dict(key=key,lon=round(tx*TILE,6),lat=round(ty*TILE,6),features=[]))
        sx=111320*math.cos(math.radians(tile['lat']))
        feature=dict(id=ident,kind=kind,name=tags.get('name:ko',tags.get('name','')),ref=tags.get('ref',''),subtype=tags.get('highway',tags.get('railway','')),**dimensions(tags,kind))
        def xy(p):return [round((p[0]-tile['lon'])*sx,3),round((p[1]-tile['lat'])*111320,3)]
        if rings:
            points=[];ends=[]
            for ring in rings:
                coords=list(ring)
                if coords[0]==coords[-1]:coords=coords[:-1]
                points.extend(xy(p) for p in coords);ends.append(len(points))
            if len(points)<3:return
            # Constrained polygon triangulation retains concave boundaries and holes.
            tri=mapbox_earcut.triangulate_float64(np.asarray(points,dtype=np.float64),np.asarray(ends,dtype=np.uint32)).tolist()
            if not tri:self.errors['untriangulated_polygon']+=1;return
            feature.update(points=[v for p in points for v in p],rings=ends,triangles=tri)
        else:
            feature.update(points=[v for p in geometry.coords for v in xy(p)],rings=[],triangles=[])
        tile['features'].append(feature);self.seen.add(ident);self.counts[kind]+=1
        countkey={'r':'roads','b':'buildings','e':'entrances','p':'platforms','rail':'rails'}[kind]
        for i in matches:
            s=self.stations[i];s[countkey]+=1
            if key not in s['tiles']:s['tiles'].append(key)
    def node(self,n):
        if n.tags.get('railway')=='subway_entrance':
            self.processed['entrance_nodes']+=1
            if n.location.valid():self.add('n'+str(n.id),'e',dict(n.tags),Point(n.location.lon,n.location.lat))
    def way(self,w):
        t=w.tags;kind=None
        if t.get('railway')=='platform' or t.get('public_transport')=='platform':
            if w.is_closed():return
            # Public-transport platform bus stops are not railway platforms.
            if t.get('railway')=='platform' or t.get('train')=='yes' or t.get('subway')=='yes':kind='p'
        elif t.get('highway') and t.get('area')!='yes':kind='r'
        elif t.get('railway') in ['rail','subway','light_rail','monorail','tram']:kind='rail'
        if not kind:return
        self.processed['ways']+=1;self.progress()
        try:
            coords=[(n.lon,n.lat) for n in w.nodes]
            if len(coords)>=2:self.add('w'+str(w.id),kind,dict(t),LineString(coords))
        except osmium.InvalidLocationError:self.errors['way_missing_node']+=1
    def area(self,a):
        t=a.tags
        kind='b' if t.get('building') not in [None,'no'] else 'p' if t.get('railway')=='platform' or t.get('public_transport')=='platform' and (t.get('train')=='yes' or t.get('subway')=='yes') else 'r' if t.get('highway') in ['pedestrian','footway'] else None
        if kind is None:return
        self.processed['areas']+=1;self.progress()
        for k,outer in enumerate(a.outer_rings()):
            try:
                shell=[(n.lon,n.lat) for n in outer];holes=[[(n.lon,n.lat) for n in r] for r in a.inner_rings(outer)]
                geometry=Polygon(shell,holes)
                if geometry.is_empty or not geometry.is_valid:
                    self.errors['invalid_area']+=1;continue
                ident=('w' if a.from_way() else 'rel')+str(a.orig_id())+('' if k==0 else '_'+str(k))
                self.add(ident,kind,dict(t),geometry,[shell]+holes)
            except osmium.InvalidLocationError:self.errors['area_missing_node']+=1

def download():
    CACHE.mkdir(parents=True,exist_ok=True)
    if SOURCE.exists() and SOURCE.stat().st_size>10_000_000:return
    part=SOURCE.with_suffix('.pbf.part')
    subprocess.run(['curl','--fail','--location','--retry','3','--retry-delay','10','--connect-timeout','20','-o',str(part),URL],check=True)
    part.replace(SOURCE)

def main():
    arg=argparse.ArgumentParser();arg.add_argument('--download',action='store_true');arg.add_argument('--radius',type=float,default=450);args=arg.parse_args()
    if args.download:download()
    if not SOURCE.exists():raise SystemExit('Source missing; rerun with --download. No fabricated coverage is emitted.')
    stations,inputs=network();print('Visitable railway stations:',len(stations),flush=True)
    e=Extract(stations,args.radius);start=time.time();cache=CACHE/'locations.cache'
    try:e.apply_file(str(SOURCE),locations=True,idx='sparse_file_array,'+str(cache))
    finally:
        if cache.exists():cache.unlink()
    OUT.mkdir(parents=True,exist_ok=True)
    for old in OUT.glob('tile_*.bytes'):old.unlink()
    for key,tile in sorted(e.tiles.items()):
        raw=json.dumps(tile,ensure_ascii=False,separators=(',',':')).encode()
        (OUT/('tile_'+key+'.bytes')).write_bytes(gzip.compress(raw,compresslevel=9,mtime=0))
    with osmium.io.Reader(str(SOURCE)) as reader:timestamp=reader.header().get('osmosis_replication_timestamp')
    manifest=dict(version=1,radius=args.radius,source=URL,sourceTimestamp=timestamp,license='ODbL-1.0',stations=stations)
    (OUT/'index.json').write_text(json.dumps(manifest,ensure_ascii=False,separators=(',',':')))
    receipt=dict(generated=datetime.datetime.now(datetime.timezone.utc).isoformat(),source=URL,sourceTimestamp=timestamp,sourceBytes=SOURCE.stat().st_size,sourceSHA256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),networkSHA256=inputs,radiusMeters=args.radius,stations=len(stations),stationsWithRoads=sum(s['roads']>0 for s in stations),stationsWithBuildings=sum(s['buildings']>0 for s in stations),stationsWithEntrances=sum(s['entrances']>0 for s in stations),stationsWithPlatforms=sum(s['platforms']>0 for s in stations),stationsWithAnyFeatures=sum(bool(s['tiles']) for s in stations),plannedOnly=sum(s['plannedOnly'] for s in stations),approximateStationCentres=sum(s['approximate'] for s in stations),tiles=len(e.tiles),features=dict(e.counts),scanErrors=dict(e.errors),elapsedSeconds=round(time.time()-start,1),runtimeBytes=sum(p.stat().st_size for p in OUT.glob('*') if p.suffix!='.meta'))
    (HERE/'coverage.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2))
    (HERE/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2))
    print(json.dumps(receipt,ensure_ascii=False,indent=2),flush=True)
if __name__=='__main__':main()
