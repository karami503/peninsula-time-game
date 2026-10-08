"""Record manually reviewed official diagram evidence; never overwrite the published CSV rows."""
import json,pathlib,hashlib
H=pathlib.Path(__file__).resolve().parent
S='https://data.seoul.go.kr/dataList/OA-11984/F/1/datasetView.do'
rows=[]
def add(region,line,station,floors,quality,note,file,url,ptype=''):
 b=(H/file).read_bytes()
 rows.append(dict(region=region,lineName=line,stationName=station,verifiedPlatformFloors=floors,floorQuality=quality,floorQualityNote=note,floorEvidenceFile=file,floorEvidenceURL=url,evidenceSHA256=hashlib.sha256(b).hexdigest(),reviewedPlatformType=ptype,reviewed='2026-10-08'))
def kric(station,code,floors,quality,note,ptype=''):
 add('수도권','경의중앙선',station,floors,quality,note,'cache/kric-'+station+'-map.png','https://station.kric.go.kr/v2/altmInfoSys/index.do?areCd=01&lnCd=K4&railOprIsttCd=KR&prprStinCd='+code,ptype)
kric('문산','0207',[1],'source-conflict','CSV 역층은 1~4F지만 KRIC 안내도에는 모든 승강장이 1F, 대합실이 4F로 표시됨. 원본 오류를 별도 보존.','섬식')
kric('운천','1135',[1],'source-conflict','CSV 역층은 1~4F지만 KRIC 안내도의 열차 타는 곳은 1F로 표시됨.')
kric('가좌','0010',[-4,1],'multi-level-reviewed','KRIC 안내도에서 서울역 지선 승강장 1F와 홍대입구 방면 승강장 B4를 확인. 복수 층은 정상임.')
for st,code,floors in [('대전','104',[-5]),('중앙로','105',[-5]),('중구청','106',[-5]),('서대전네거리','107',[-4])]:
 file='cache/djtc-daejeon-map.png' if code=='104' else 'cache/djtc-'+code+'-map.png'
 add('대전','대전1호선',st,floors,'source-conflict','대전교통공사 VR 안내도에서 승강장 층을 확인. CSV에는 위층 연결층도 방향별 승강장 층으로 기재됨.',file,'https://www.djtc.kr/stationVR/'+code+'-vtour/tour.html')
for line,floors in [('1호선',[1]),('5호선',[-4])]:
 add('수도권',line,'신길',floors,'source-conflict','서울교통공사 환승 안내도: 1호선 승강장 1F·대합실 2F, 5호선 승강장 B4·대합실 B3. CSV 승강장 층과 충돌.','cache/plans/seoul/5호선/525 신길(3차수정).jpg',S)
for line,floors in [('2호선',[-2]),('신분당선',[-4])]:
 add('수도권',line,'강남',floors,'diagram-reviewed','서울교통공사 강남역 안내도: 2호선 B2 상대식, 신분당선 B4 상대식 승강장.','cache/plans/seoul/2호선/222 강남역.jpg',S,'상대식')
for line,floors in [('5호선',[-3]),('9호선',[-4,-3]),('공항철도',[-4,-3]),('김포골드라인',[-4]),('서해선',[-5])]:
 add('수도권',line,'김포공항',floors,'multi-level-reviewed' if len(floors)>1 else 'diagram-reviewed','서울교통공사 김포공항역 안내도에서 해당 노선의 승강장 층 확인. 복수 층은 방향별 분리 승강장이며 하나로 합치지 않음.','cache/plans/seoul/5호선/512 김포공항.jpg',S)
(H/'floor-reviews.json').write_text(json.dumps(dict(retrieved='2026-10-08',method='Human visual review of explicit platform labels in official diagrams. Floor labels never authorize depth or geometry.',rows=rows),ensure_ascii=False,indent=2))
print('reviewed station-line keys',len(rows))
