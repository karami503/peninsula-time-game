# 창원 오픈월드 자료 (Changwon open-world data)

`PeninsulaTimeUnity/Assets/Resources/Changwon/`의 파일은 이 폴더의 스크립트로 만듭니다. 원본 내려받기 자료(수백 MB)는 저장소에 넣지 않습니다.

## 출처와 라이선스

| 자료 | 출처 | 라이선스 |
|---|---|---|
| 지형 고도 | AWS Terrain Tiles (terrarium, z13/12/11, SRTM 등 합성) `s3.amazonaws.com/elevation-tiles-prod` | 공개 자료 (출처 표기) |
| 건물 윤곽·높이·층수, 도로·철도 구간과 교차점, 다리·터널 구간, 장소, 수역·토지 이용·피복, 행정 경계 | Overture Maps Foundation release 2026-09-23.1 (`overturemaps-us-west-2` S3) | ODbL (OpenStreetMap 기반) / CDLA Permissive 2.0 |
| 시내버스 노선·정류장 | 창원시 버스정보시스템 (`Resources/Geo/ChangwonBuses.txt`, 기존 가져오기) | 공공 자료 |
| 명소 52곳 | 웹 조사 2026-10-08, 항목별 출처는 `landmarks_research.json` | — |

높이가 기록되지 않은 건물은 용도·면적·형태로 추정한 높이입니다. 고도 자료는 건물·수목이 섞인 표면 모델이라 시가지는 형태학적 열림과 평활화로 다듬었습니다. 실측 설계도 수준의 재현이 아닙니다.

## 만드는 순서

```sh
D=/tmp/changwon-data   # 내려받기 자료 폴더 (저장소 밖)
mkdir -p $D/terrain $D/terrain12 $D/terrain11 $D/overture
python3 fetch_terrain.py $D/terrain 13; python3 fetch_terrain.py $D/terrain12 12; python3 fetch_terrain.py $D/terrain11 11
python3 fetch_overture.py $D/overture divisions division_points water land_use land infrastructure places segments connectors land_cover buildings
python3 build_terrain.py $D $D/height.npy                 # 16 m 고도 격자 (경도 128.33–128.86, 위도 35.01–35.41)
python3 build_land.py $D/overture $D/height.npy $D/cw      # 토지 분류, 해안선, 호수, 시가지 평탄화
python3 build_world.py $D ../../PeninsulaTimeUnity/Assets  # 지형·건물·도로·장소·호수·버스·지도
python3 build_areas.py $D ../../PeninsulaTimeUnity/Assets  # 구·동 이름
python3 build_landmarks.py ../../PeninsulaTimeUnity/Assets # 명소
```

필요한 파이썬 패키지는 numpy, pillow, shapely, scipy, pyarrow입니다. `fetch_overture.py`는 HTTP 범위 요청을 재시도하므로 프록시 환경에서도 받을 수 있습니다.

## 파일 형식 (`ChangwonData.cs`가 읽음)

좌표는 x=동쪽, z=북쪽, 원점 128.62°E 35.20°N, 단위 미터입니다. y는 해발 미터이고 바다는 y=0 평면입니다.

- `cw_terrain.bytes` (gzip): `CWT1`, NX, NZ, X0, Z0, CELL(16), 행마다 차분 부호화한 int16 고도(0.1 m), 토지 분류 byte 격자.
- `cw_buildings.bytes` (gzip): `CWB1`, 512 m 청크 격자, 청크별 오프셋, 건물 레코드(종류, 층, 높이, 바닥 높이, 이름, 윤곽 int16×2 0.05 m 단위).
- `cw_roads.bytes` (gzip): `CWR1`, 교차점, 도로·철도 구간(종류, 일방통행·연결로 플래그, 폭, 차로, 이름, 점마다 x/y/z와 지상·다리·터널 구분), 청크별 구간 목록. 다리와 터널의 높이는 양 끝의 지상 높이로 보간하고, 지상 도로 아래 지형은 도로 높이에 맞춰 깎거나 채웠습니다.
- `cw_places.txt`: `x|z|kind|name` (편의점·카페·식당·주유소·시장 등), `i:`로 시작하는 kind는 버스 정류장·신호등 같은 시설.
- `cw_lakes.txt`: 호수·저수지·하천 윤곽과 수면 높이.
- `cw_buses.bytes` (gzip 텍스트): 191개 창원 BIS 노선의 주행 경로(x,y,z)와 정류장.
- `cw_areas.txt`: 5개 구 경계와 동·읍·면·리 위치.
- `cw_landmarks.txt`: 명소 52곳.
- `cw_map.png`: 4096 px 전체 지도(256색).
