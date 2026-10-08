"""Validate downloaded public sources, images and the complete game station audit."""
import json,pathlib,hashlib,imghdr
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parent.parent
data=json.loads((ROOT/'PeninsulaTimeUnity/Assets/Resources/Geo/StationPlans/index.json').read_text())
coverage=json.loads((HERE/'coverage.json').read_text())
stationdata=json.loads((ROOT/'PeninsulaTimeUnity/Assets/Resources/Geo/StationAreas/index.json').read_text())
stations={r['id']:r for r in stationdata['stations']}
sources=json.loads((HERE/'sources.json').read_text())['rows']+json.loads((HERE/'structure-sources.json').read_text())['rows']
sourceids={s['id'] for s in sources}
for s in sources:
    content=(HERE/s['file']).read_bytes()
    assert len(content)==s['bytes'] and hashlib.sha256(content).hexdigest()==s['sha256'],s['id']
plans=json.loads((HERE/'plans.json').read_text())['rows']+json.loads((HERE/'busan-plans.json').read_text())['rows']
for p in plans:
    content=(HERE/p['file']).read_bytes()
    assert len(content)==p['bytes'],p['file']
    assert imghdr.what(None,content) in ('jpeg','png','gif') or content.startswith(b'\xff\xd8\xff'),p['file']
    if 'sha256' in p:assert hashlib.sha256(content).hexdigest()==p['sha256'],p['file']
assert len({(r['stationId'],r['lineId']) for r in data['rows']})==len(data['rows'])
assert set(stations)=={r['stationId'] for r in coverage['stations']}
for r in data['rows']:
    assert r['stationId'] in stations and r['lineId'] in stations[r['stationId']]['lines']
    assert r['name']==stations[r['stationId']]['name']
    assert set(r['sourceIds'])<=sourceids
    assert r['hasPlatformFloor']==(len(r['platformFloors'])==1)
    if r['hasPlatformFloor']:assert r['platformFloor']==r['platformFloors'][0]
    else:assert r['platformFloor']==0
    assert r['sourceURL'].startswith('https://')
    if r['planCount']:assert r['planURL'].startswith('https://')
    assert r['floorGeometryAllowed'] is False
    assert r['floorQuality'] in ('published-unreviewed','review-required','source-conflict','diagram-reviewed','multi-level-reviewed','missing')
    if r['verifiedPlatformFloors']:
        assert r['floorEvidenceURL'].startswith('https://') and r['verifiedPlatformFloorLabel']
        assert r['floorQuality'] in ('source-conflict','diagram-reviewed','multi-level-reviewed')
    else:assert r['floorQuality'] in ('published-unreviewed','review-required','missing')
reviews=json.loads((HERE/'floor-reviews.json').read_text())['rows']
for review in reviews:
    raw=(HERE/review['floorEvidenceFile']).read_bytes()
    assert hashlib.sha256(raw).hexdigest()==review['evidenceSHA256']
    assert imghdr.what(None,raw) in ('jpeg','png','gif') or raw.startswith(b'\xff\xd8\xff'),review['floorEvidenceFile']
floor_audit=json.loads((HERE/'floor-quality-audit.json').read_text())
assert len(floor_audit['headers'])==len(sources)
assert all(h['floorColumn']=='역층' and h['gradeColumn']=='지상구분' for h in floor_audit['headers'])
def reviewed(name,line):return [r for r in data['rows'] if r['name']==name and r['lineName']==line]
assert all(r['platformFloors']==[1,2,3,4] and r['verifiedPlatformFloors']==[1] for r in reviewed('문산','경의·중앙선'))
assert all(r['platformFloors']==[-5,-4] and r['verifiedPlatformFloors']==[-5] for r in reviewed('대전','대전 1호선'))
assert all(r['platformType']=='상대식' and r['reviewedPlatformType']=='상대식' for r in reviewed('강남','2호선'))
assert not any(r['exactInterior'] for r in coverage['stations'])
print(json.dumps(dict(passed=True,sourceFiles=len(sources),referenceImages=len({p['file'] for p in plans}|{r['floorEvidenceFile'] for r in reviews}),stationLinePairs=len(data['rows']),auditedStationIds=len(stations),reviewedStationLineKeys=len(reviews),stats=data['stats']),ensure_ascii=False,indent=2))
