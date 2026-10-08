from pathlib import Path
import json,gzip,math
r=Path(__file__).resolve().parents[2]/'PeninsulaTimeUnity';root=r.parent/'AssetSources/OSM/changwon-bis';stations={};lines=[];worst=0
for route in json.loads((root/'routes.json').read_text()):
 rid=route['routeId'];stops=json.load(gzip.open(root/f'{rid}-getRouteView.json.gz'))['rows'];geom=json.load(gzip.open(root/f'{rid}-getLine.json.gz'))['rows'];stops.sort(key=lambda s:s['spotSn'])
 unique=[]
 for stop in stops:
  sid='cw-stop-'+str(stop['spotId']);stations[sid]=(stop['routeName'],stop['xCrdnt'],stop['yCrdnt'])
  if not unique or unique[-1]['spotId']!=stop['spotId']:unique.append(stop)
 stops=unique;ids=['cw-stop-'+str(s['spotId']) for s in stops]
 nodes=[]
 for p in sorted((p for p in geom if p.get('linkId')),key=lambda p:(p['spotSn'],p.get('sn',0))):
  xy=(p['xCrdnt'],p['yCrdnt'])
  if not nodes or nodes[-1][0]!=xy:nodes.append((xy,p['spotSn']))
 if len(nodes)<2:raise ValueError('Missing road shape '+str(rid))
 markers={};cursor=0;scale=math.cos(math.radians(stops[0]['yCrdnt']))
 for si,stop in enumerate(stops):
  p=(stop['xCrdnt'],stop['yCrdnt']);sn=stop['spotSn'];before=max((order for xy,order in nodes if order<=sn),default=nodes[0][1]);after=min((order for xy,order in nodes if order>=sn),default=nodes[-1][1]);best=None
  for k in range(cursor,len(nodes)-1):
   if nodes[k][1] not in (before,after) and nodes[k+1][1] not in (before,after):continue
   a,b=nodes[k][0],nodes[k+1][0];dx,dz=(b[0]-a[0])*scale,b[1]-a[1];u=max(0,min(1,((p[0]-a[0])*scale*dx+(p[1]-a[1])*dz)/max(1e-20,dx*dx+dz*dz)));q=(a[0]+(b[0]-a[0])*u,a[1]+(b[1]-a[1])*u);d=((p[0]-q[0])*scale)**2+(p[1]-q[1])**2
   if best is None or d<best[0]:best=(d,k,u,q)
  if best is None:raise ValueError('Unmatched ordered stop '+str(rid)+' '+stop['routeName'])
  d,k,u,q=best;worst=max(worst,math.sqrt(d)*111320);cursor=k;markers.setdefault(k,[]).append((u,si,q))
 shape=[];indices=[0]*len(stops)
 for k,(p,_) in enumerate(nodes):
  if not shape or shape[-1]!=p:shape.append(p)
  for u,si,q in sorted(markers.get(k,[])):
   if math.dist(shape[-1],q)>1e-8:shape.append(q)
   indices[si]=len(shape)-1
 if any(a>b for a,b in zip(indices,indices[1:])):raise ValueError('Nonmonotonic stops '+str(rid))
 lid='cw-bus-'+str(rid);color={'1':'#CB3C3F','2':'#278AC5','3':'#4B9B52'}.get(route['routeColorCode'],'#278AC5');name=route['routeNo']
 lines.extend([f'L|{lid}|bus|창원 {name}번 ({route["startRoute"]}–{route["endRoute"]})|{name}|{color}|1|1|0000|2400|22|0|창원 버스',f'P|{lid}|'+ ' '.join(ids),f'G|{lid}|'+ ';'.join(f'{x:.7f},{y:.7f}' for x,y in shape),f'J|{lid}|'+ ' '.join(map(str,indices)),f'Q|{lid}|existing|https://bus.changwon.go.kr · 2026-10-08'])
out=['# Changwon BIS public route data, retrieved 2026-10-08. Headways are game settings.']+[f'S|{sid}|{n}|{x:.7f}|{y:.7f}' for sid,(n,x,y) in stations.items()]+lines
(r/'Assets/Resources/Geo/ChangwonBuses.txt').write_text('\n'.join(out)+'\n');print('Imported',len(lines)//5,'routes',len(stations),'stops; max stop/road separation',round(worst,1),'m')
