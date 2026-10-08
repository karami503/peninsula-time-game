from pathlib import Path
import gzip,json,math,collections
r=Path(__file__).resolve().parents[2]/'PeninsulaTimeUnity';src=r.parent/'AssetSources/OSM/transit'
segments=collections.defaultdict(list)
seen=set()
for f in ['metro_ways','main_rail']:
 for w in json.load(gzip.open(src/(f+'.json.gz')))['elements']:
  if w['id'] in seen:continue
  seen.add(w['id']);t=w.get('tags',{});g=w.get('geometry',[])
  layer=float(t.get('layer','0')) if t.get('layer','0').lstrip('-').isdigit() else 0
  grade='underground' if (t.get('tunnel') not in (None,'no') or layer<0) else 'elevated' if (t.get('bridge') not in (None,'no') or layer>0) else 'surface'
  for a,b in zip(g,g[1:]):
   seg=(a['lon'],a['lat'],b['lon'],b['lat'],grade,w['id'])
   for x in range(math.floor(min(a['lon'],b['lon'])*100),math.floor(max(a['lon'],b['lon'])*100)+1):
    for y in range(math.floor(min(a['lat'],b['lat'])*100),math.floor(max(a['lat'],b['lat'])*100)+1):segments[x,y].append(seg)
rows=['# Station grade inferred from nearest OSM railway within 100 m; not a surveyed floor plan.'];counts=collections.Counter()
for s in json.load(gzip.open(src/'stations.json.gz'))['elements']:
 if s['type']!='node':continue
 lon,lat=s['lon'],s['lat'];scale=math.cos(math.radians(lat));best=(100**2,None,None)
 for dx in [-1,0,1]:
  for dy in [-1,0,1]:
   for x1,y1,x2,y2,grade,way in segments.get((int(lon*100)+dx,int(lat*100)+dy),[]):
    ax,az=(x1-lon)*111320*scale,(y1-lat)*111320;bx,bz=(x2-x1)*111320*scale,(y2-y1)*111320
    u=max(0,min(1,-(ax*bx+az*bz)/max(.001,bx*bx+bz*bz)));d=(ax+u*bx)**2+(az+u*bz)**2
    if d<best[0]:best=(d,grade,way)
 if best[1]:rows.append(f'H|r{s["id"]}|{best[1]}|https://www.openstreetmap.org/way/{best[2]}');counts[best[1]]+=1
(r/'Assets/Resources/Geo/StationGrades.txt').write_text('\n'.join(rows)+'\n');print(counts)
