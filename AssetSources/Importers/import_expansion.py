from pathlib import Path
import gzip,json,math
r=Path(__file__).resolve().parents[2]/'PeninsulaTimeUnity';res=r/'Assets/Resources/Geo';base=(res/'TransitNetwork.txt').read_text();lookup={};raw={}
for line in base.splitlines():
 f=line.split('|')
 if f[0]=='S':lookup.setdefault(f[2].removesuffix('역'),[]).append((f[1],float(f[3]),float(f[4])))
for e in json.loads(gzip.decompress((r.parent/'AssetSources/OSM/transit/stations.json.gz').read_bytes()))['elements']:
 name=e.get('tags',{}).get('name:ko',e.get('tags',{}).get('name','')).removesuffix('역')
 raw.setdefault(name,[]).append(('r'+str(e['id']),e['lon'],e['lat']))
routes=json.loads(Path(__file__).with_name('plan-routes.json').read_text())
routes['GTX-F'][0]='의정부 탑석 풍양 왕숙 왕숙2 덕소 교산 복정 모란 정자 기흥 수원 오목천 야목 초지 시흥시청 신천 부천종합운동장 김포공항 대곡 장흥 의정부'.split()
# Approximate scenario anchors where no existing station exists. Not presented as approved station sites.
anchors={'경남도청':(128.692,35.238),'경화':(128.688,35.159),'남창원':(128.664,35.206),'성주사':(128.694,35.190),'신창원':(128.632,35.236),'진해':(128.661,35.153),'월영광장':(128.562,35.181),'진해구청':(128.711,35.133),'창곡':(128.658,35.219),'창원광장':(128.682,35.228),'마산어시장':(128.576,35.203),'마산자유무역지역':(128.589,35.220),
'광명시흥':(126.856,37.439),'교산':(127.202,37.519),'대장':(126.789,37.543),'동의정부':(127.089,37.738),'숭우리':(127.137,37.830),'신정릉':(127.005,37.601),'왕숙':(127.166,37.650),'왕숙2':(127.194,37.609),'위례':(127.145,37.477),'창릉':(126.894,37.645),'청학':(126.677,37.425),'풍양':(127.151,37.721),'장흥':(126.948,37.715)}
rows=['# Expansion scenarios 0.9.4; user supplied route diagrams, 2024 Changwon council plan, OSM stations.',' # Pending stations are approximate scenario anchors, never a surveyed approved location.']
created=set();stationids={}
def resolve(name,region):
 key=(name,region)
 if key in stationids:return stationids[key]
 candidates=lookup.get(name,raw.get(name,[]));target=(128.65,35.22) if region=='창원 계획' else (127,37.5)
 if candidates:
  match=min(candidates,key=lambda c:(c[1]-target[0])**2+(c[2]-target[1])**2);sid,x,y=match
  if name not in lookup and sid not in created:rows.append(f'S|{sid}|{name}|{x:.7f}|{y:.7f}');created.add(sid)
 else:
  if name not in anchors:raise ValueError('Unresolved '+name)
  x,y=anchors[name];sid='scenario-'+name
  if sid not in created:rows.extend([f'S|{sid}|{name}|{x:.7f}|{y:.7f}',f'E|{sid}|approximate']);created.add(sid)
 stationids[key]=sid;return sid
for ix,(name,(names,color)) in enumerate(routes.items()):
 region='창원 계획' if name.startswith('창원') else '수도권 계획';ids=[resolve(n,region) for n in names];lid='expansion-'+str(ix)
 planned='자기부상' not in name
 rows += [f'L|{lid}|metro|{name}'+(' · 계획 시나리오' if planned else '')+f'|{name}|{color}|0.5|0.5|0000|2400|55|{int(name=="GTX-F")}|{region}', f'P|{lid}|'+ ' '.join(ids),f'Q|{lid}|'+('planned' if planned else 'existing')+'|사용자 제공 노선도 · 2024 창원시의회 업무계획 · OpenStreetMap']
(res/'TransitExpansion.txt').write_text('\n'.join(rows)+'\n')
print('Expansion routes',len(routes),'new stations',len(created))
