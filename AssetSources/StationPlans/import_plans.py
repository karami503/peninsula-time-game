"""Compile official platform-floor metadata and per-game-ID coverage, with strict line/name matching."""
import csv,json,pathlib,re,hashlib,collections
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parent.parent
RES=ROOT/'PeninsulaTimeUnity/Assets/Resources/Geo/StationPlans';RES.mkdir(parents=True,exist_ok=True)

def readcsv(path):
 b=path.read_bytes()
 for enc in ['utf-8-sig','cp949']:
  try:return list(csv.DictReader(b.decode(enc).splitlines()))
  except UnicodeDecodeError:pass
 raise ValueError(path)

def name(n):
 n=re.sub(r'\([^)]*\)','',n).strip().replace('·','').replace('ㆍ','').replace(' ','')
 return n[:-1] if n.endswith('역') else n

def linename(s):
 s=s.replace(' ','').replace('·','')
 return {'공항':'공항철도','경춘':'경춘선','경의중앙':'경의중앙선','수인분당':'수인분당선','신분당':'신분당선','의정부':'의정부경전철','에버라인':'용인에버라인','동해':'동해선','경강':'경강선','서해':'서해선','우이신설':'우이신설선'}.get(s,s)
def rowkey(row):
 op=row['철도운영기관명'];region='수도권'
 for place in ['부산','대구','대전','광주']:
  if place in op:region=place;break
 line=linename(row['선명'])
 if line=='동해선':region='부산'
 if line=='대경선':region='대구'
 if region!='수도권' and re.fullmatch(r'[1-4]호선',line):line=region+line
 return region,line,name(row['역명'])

source=json.loads((HERE/'sources.json').read_text())['rows'];candidates=collections.defaultdict(list)
headers=[]
structures=json.loads((HERE/'structure-sources.json').read_text())['rows'] if (HERE/'structure-sources.json').exists() else []
for s in source+structures:
 if 'error'in s:continue
 d=json.loads((HERE/'cache'/(s['id']+'-download.json')).read_text())
 title=d['dataSetFileDetailInfo'].get('publicDataSj','')
 dates=re.findall(r'20\d{6}',title);s['sourceDate']=dates[-1] if dates else s['published'].replace('-','')
 rawrows=readcsv(HERE/s['file'])
 fieldnames=[k.strip() for k in rawrows[0] if k] if rawrows else []
 assert {'철도운영기관명','선명','역명','지상구분','역층'}<=set(fieldnames),(s['id'],fieldnames)
 headers.append(dict(sourceId=s['id'],fieldnames=fieldnames,floorColumn='역층',gradeColumn='지상구분',platformNumberColumn='승강장번호' if '승강장번호' in fieldnames else None))
 for row in rawrows:
  row={k.strip():v.strip() if v else '' for k,v in row.items() if k}
  if row.get('역층구분') and row['역층구분']!='승강장':continue
  if row.get('역명') and row.get('선명'):candidates[rowkey(row)].append((s,row))
(HERE/'sources.json').write_text(json.dumps(dict(retrieved='2026-10-08',rows=source),ensure_ascii=False,indent=2))

arch={}
for a in readcsv(ROOT/'AssetSources/StationAreas/metro-architecture-20251231.csv'):
 arch[('수도권',a['호선']+'호선',name(a['역명']))]=a

planmatches=collections.defaultdict(list)
plans=json.loads((HERE/'plans.json').read_text())['rows']
for p in plans:
 filename=p['filename'];line=re.search(r'([1-8])호선',filename).group(1)+'호선'
 stem=pathlib.Path(filename).stem
 for key in set(candidates)|set(arch):
  if key[0]!='수도권' or key[1]!=line:continue
  n=key[2]
  # Operator archives use numbered Korean filenames; only whole Korean tokens may match.
  if re.search(r'(?<![가-힣])'+re.escape(n)+r'(?:역)?(?![가-힣])',stem):planmatches[key].append(p)
if (HERE/'busan-plans.json').exists():
 for p in json.loads((HERE/'busan-plans.json').read_text())['rows']:
  if not p.get('error') and p.get('bytes',0)>200:planmatches[('부산','부산'+str(p['line'])+'호선',name(p['name']))].append(p)

lines={}
for f in ['TransitNetwork','TransitExpansion']:
 for t in (ROOT/('PeninsulaTimeUnity/Assets/Resources/Geo/'+f+'.txt')).read_text().splitlines():
  a=t.split('|')
  if a[0]=='L':lines[a[1]]=dict(kind=a[2],shortName=a[4],region=a[-1])
manifest=json.loads((ROOT/'PeninsulaTimeUnity/Assets/Resources/Geo/StationAreas/index.json').read_text())
reviews={(r['region'],r['lineName'],r['stationName']):r for r in json.loads((HERE/'floor-reviews.json').read_text())['rows']}
def floorlabel(floors):return ' / '.join(('B'+str(-f) if f<0 else str(f)+'F') for f in floors)
records=[];audit=[];conflicts=[]
for station in manifest['stations']:
 stationrecords=[]
 for lid in station['lines']:
  line=lines.get(lid,{})
  if not line:continue
  key=(line['region'],linename(line['shortName']),name(station['name']))
  entries=candidates.get(key,[]);ar=arch.get(key);ps=planmatches.get(key,[])
  review=reviews.get(key)
  if not entries and not ar and not ps and not review:continue
  latest=max((s['sourceDate'] for s,r in entries),default='')
  chosen=[(s,r) for s,r in entries if s['sourceDate']==latest]
  floors=set()
  for s,r in chosen:
   try:
    floor=int(r['역층']);grade=r['지상구분'];assert floor>0 and grade in ['지상','지하'];floors.add(-floor if grade=='지하' else floor)
   except (ValueError,KeyError,AssertionError):pass
  conflict=len(floors)>1
  # Both levels can be real (e.g. cross-platform stacked stations); retain all and never flatten.
  floorlist=sorted(floors);sourceids=sorted(set(s['id'] for s,r in chosen))
  record=dict(stationId=station['id'],lineId=lid,name=station['name'],lineName=line['shortName'],hasPlatformFloor=len(floors)==1,platformFloor=next(iter(floors)) if len(floors)==1 else 0,platformFloors=floorlist,platformFloorLabel=floorlabel(floorlist),platformType=ar['승강장유형'] if ar else '',platformLength=float(ar['길이']) if ar else 0,architectureFloorLabel=ar['층수'] if ar else '',sourceIds=sourceids,sourceDate=latest,planCount=len(ps),planURL=ps[0].get('planURL',ps[0]['source']) if ps else '',sourceURL='https://www.data.go.kr/data/'+sourceids[0]+'/fileData.do' if sourceids else ('https://www.data.go.kr/data/15044258/fileData.do' if ar else (ps[0]['source'] if ps else review['floorEvidenceURL'])),planFiles=[p['file'] for p in ps])
  record.update(floorQuality='review-required' if conflict else ('published-unreviewed' if floors else 'missing'),floorQualityNote='여러 층이 기재되어 있으며 승강장 안내도와 대조가 필요함.' if conflict else 'CSV 원문 층 표기이며 안내도와 개별 대조하지 않음.',floorEvidenceURL='',verifiedPlatformFloors=[],verifiedPlatformFloorLabel='',floorGeometryAllowed=False,reviewedPlatformType='')
  if review:
   for field in ['floorQuality','floorQualityNote','floorEvidenceURL','verifiedPlatformFloors','reviewedPlatformType']:record[field]=review[field]
   record['verifiedPlatformFloorLabel']=floorlabel(review['verifiedPlatformFloors'])
   if not ps:
    record['planCount']=1;record['planURL']=review['floorEvidenceURL']
  if not floors and not review:record['floorQualityNote']='공개 승강장 층 자료 없음.'
  records.append(record);stationrecords.append(record)
  if conflict:conflicts.append(dict(stationId=station['id'],name=station['name'],lineId=lid,floors=floorlist))
 audit.append(dict(stationId=station['id'],name=station['name'],plannedOnly=station.get('plannedOnly',False),lines=station['lines'],matchedLineIds=[r['lineId'] for r in stationrecords],hasOfficialPlatformData=any(r['platformFloors'] for r in stationrecords),hasOfficialPlan=any(r['planCount'] for r in stationrecords),missingLineIds=[lid for lid in station['lines'] if lid not in [r['lineId'] for r in stationrecords]],exactInterior=False))
for rec in records:rec.pop('planFiles') # Raw images stay offline; runtime has metadata and official source links only.
stats=dict(gameStationIds=len(audit),withOfficialPlatformData=sum(r['hasOfficialPlatformData'] for r in audit),withOfficialPlan=sum(r['hasOfficialPlan'] for r in audit),matchedStationLinePairs=len(records),multiLevelPairs=len(conflicts),publicPlatformDatasets=len(source),publicStructureDatasets=len(structures),exactInterior=0)
stats.update(diagramReviewedPairs=sum(bool(r['verifiedPlatformFloors']) for r in records),sourceConflictPairs=sum(r['floorQuality']=='source-conflict' for r in records),unreviewedFloorPairs=sum(r['floorQuality'] in ['published-unreviewed','review-required'] for r in records))
(RES/'index.json').write_text(json.dumps(dict(version=1,retrieved='2026-10-08',notice='Public floor labels and reference diagrams; not measured CAD or complete interiors.',stats=stats,rows=records),ensure_ascii=False,separators=(',',':')))
(HERE/'coverage.json').write_text(json.dumps(dict(stats=stats,multiLevelPairs=conflicts,stations=audit),ensure_ascii=False,indent=2))
(HERE/'floor-quality-audit.json').write_text(json.dumps(dict(retrieved='2026-10-08',parser='Explicit named 역층 and 지상구분 columns; 승강장번호 is never used as a floor.',headers=headers,qualityCounts=dict(collections.Counter(r['floorQuality'] for r in records)),rawSelectedRows=[dict(key=key,rows=[dict(sourceId=s['id'],row=r) for s,r in entries]) for key,entries in candidates.items() if key in reviews or key[2]=='개포동'],reviews=list(reviews.values()),geometryAllowed=False),ensure_ascii=False,indent=2))
print(json.dumps(stats,ensure_ascii=False,indent=2));print('multi levels',conflicts[:15]);print('bytes',(RES/'index.json').stat().st_size)
