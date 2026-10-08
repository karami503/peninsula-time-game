from pathlib import Path
import gzip,json,re
r=Path(__file__).resolve().parents[2]/'PeninsulaTimeUnity';folder=r.parent/'AssetSources/OSM/changwon-city';features={}
for f in folder.glob('*.json.gz'):
 for e in json.load(gzip.open(f))['elements']:features[e['id']]=e
out=[]
for e in features.values():
 g=e.get('geometry',[]);t=e.get('tags',{});p=[(v['lon'],v['lat']) for v in g]
 if len(p)<2:continue
 kind='b' if 'building' in t else 'r'
 if kind=='b' and (len(p)<4 or p[0]!=p[-1]):continue
 def num(v,default):
  m=re.match(r'[0-9.]+',str(v or ''));return float(m[0]) if m else default
 if kind=='b':size=min(200,num(t.get('height'),num(t.get('building:levels'),2 if t['building'] in ('house','residential') else 4)*3.2))
 else:
  highway=t.get('highway','');
  if highway in ('proposed','construction','steps'):continue
  size=num(t.get('width'),num(t.get('lanes'),2)*3.2 if highway not in ('path','footway','cycleway') else 2)
  if t.get('tunnel') not in (None,'no'):continue
 name=t.get('name','').replace('|',' ');cx=sum(x for x,z in p)/len(p);cz=sum(z for x,z in p)/len(p)
 out.append(f'{kind}|{e["id"]}|{cx:.7f}|{cz:.7f}|{size:.2f}|{name}|'+ ';'.join(f'{x:.7f},{z:.7f}' for x,z in p))
(r/'Assets/Resources/Geo/ChangwonCity.txt').write_text('\n'.join(out)+'\n');print(len(out),'city features')
