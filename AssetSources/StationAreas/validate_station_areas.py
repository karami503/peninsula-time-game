import collections,gzip,json,math,pathlib
ROOT=pathlib.Path(__file__).resolve().parent.parent.parent
P=ROOT/'PeninsulaTimeUnity/Assets/Resources/Geo/StationAreas'
manifest=json.loads((P/'index.json').read_text());counts=collections.Counter();heights=collections.Counter();widths=collections.Counter();ids=set();errors=[];maxfeatures=0;maxpoints=0;totalpoints=0
keys=set()
for path in sorted(P.glob('tile_*.bytes')):
 tile=json.loads(gzip.decompress(path.read_bytes()));keys.add(tile['key']);maxfeatures=max(maxfeatures,len(tile['features']))
 for f in tile['features']:
  if f['id'] in ids:errors.append('duplicate '+f['id'])
  ids.add(f['id']);counts[f['kind']]+=1;p=f['points'];totalpoints+=len(p)//2;maxpoints=max(maxpoints,len(p)//2)
  if len(p)%2 or not all(math.isfinite(v) for v in p):errors.append('points '+f['id'])
  if any(i<0 or i>=len(p)//2 for i in f['triangles']):errors.append('triangles '+f['id'])
  if f['kind']=='b':heights[f['heightSource']]+=1
  if f['kind']=='r':widths[f['widthSource']]+=1
for s in manifest['stations']:
 for t in s['tiles']:
  if t not in keys:errors.append('missing tile '+t)
print(json.dumps(dict(stations=len(manifest['stations']),tiles=len(keys),features=dict(counts),heightSources=dict(heights),roadWidthSources=dict(widths),maxFeaturesPerTile=maxfeatures,maxPointsPerFeature=maxpoints,totalPoints=totalpoints,errors=errors,stationsWithoutBuildings=[dict(id=s['id'],name=s['name']) for s in manifest['stations'] if not s['buildings']],stationsWithoutPublishedBuildings=[dict(id=s['id'],name=s['name']) for s in manifest['stations'] if not(s['buildings']-s.get('authoredBuildings',0))],stationsWithAuthoredExteriors=[dict(id=s['id'],name=s['name'],parts=s['authoredBuildings']) for s in manifest['stations'] if s.get('authoredBuildings',0)]),ensure_ascii=False,indent=2))
assert not errors
