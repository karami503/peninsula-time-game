"""Import the public Seoul Metro CSV; no guessed interior width/depth added."""
import csv,hashlib,json,pathlib
HERE=pathlib.Path(__file__).resolve().parent
source=HERE/'metro-architecture-20251231.csv'
rows=[]
for r in csv.DictReader(source.read_text(encoding='cp949').splitlines()):
 rows.append(dict(line=int(r['호선']),name=r['역명'],platformType=r['승강장유형'],platformLength=float(r['길이']),floorLabel=r['층수'],totalStationArea=float(r['면적']),year=r['준공연도']))
data=dict(source='https://www.data.go.kr/data/15044258/fileData.do',sourceDate='2025-12-31',downloadURL='https://www.data.go.kr/cmm/cmm/fileDownload.do?atchFileId=FILE_000000003621785&fileDetailSn=1&insertDataPrcus=N',sourceSHA256=hashlib.sha256(source.read_bytes()).hexdigest(),rows=rows)
out=HERE.parent.parent/'PeninsulaTimeUnity/Assets/Resources/Geo/StationAreas/architecture.json'
out.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')))
print(len(rows),'official station-line architecture rows imported')
