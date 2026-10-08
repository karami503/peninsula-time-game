# 0.9.7 전국 역 주변 실제 지도 자료

> **0.9.8 보완:** 이 문서의 OSM 수집 수치는 0.9.7 원본 기록이다. 이후 공개 건물 윤곽 417개를 6곳에 추가하여 공개 건물 자료가 있는 역 ID가 1,101개가 되었다. 오수·반성에는 공개 윤곽 대신 사진을 참고한 추정 외관 2세트·7부품을 별도 제작했다. 외관 표현이 있는 게임 역 ID는 1,103개이며 공개 자료와 추정 제작을 구분한다. 출처·정확도·재생성 절차는 [누락 8곳 조사·보완](MISSING_STATIONS_098.md)을 참고한다.

## 범위와 수집 완료 수치

게임 `TransitNetwork`, `TransitExpansion`, `ChangwonBuses`, `StationGrades`를 실제 런타임 규칙대로 합친 뒤, 유효한 철도 노선의 정차역 ID를 기준으로 삼았다. 버스/BRT만 있는 정류장은 제외했다. 1,103개 **역 ID**이며 환승역 중복 ID 및 계획 시나리오를 포함한다. 현실의 전국 역 수를 뜻하지 않는다. 계획 노선만 서는 ID는 25개, 기존 데이터에서 위치가 approximate인 ID는 24개다.

- 대상 1,103 / 주변 도형이 하나 이상 있는 역 1,103
- 실제 도로 중심선: 1,103개 역 주변, 중복 제거 후 153,696개 도형
- 실제 건물 윤곽: 1,095개 역 주변, 197,122개 도형
- OSM 지하철 출입구 위치: 937개 역 주변, 4,486개 점
- 철도 승강장 도형: 402개 역 주변, 1,159개 도형
- 선로 도형: 8,733개
- 역별 반경 450m와 교차하는 도형을 2,911개 타일에 중복 없이 저장했다. 원형 선택 경계는 96분할 다각형으로 근사한다. 경계에 걸친 건물은 원래 윤곽 전체를 보존한다.
- 실행용 원본 압축 자료는 22,994,834바이트(약 21.9MiB, 공식 건축정보 추가 전)다. 모든 역의 자료를 한 번에 메모리에 올리지 않는다.

`StationAreas/coverage.json`에 수집시각·원본 SHA256·입력 게임 데이터 SHA256·실제 수치를 기록했고, `StationAreas/manifest.json`에는 역별 도형 수와 타일 목록을 기록했다. 원본의 도형이 존재한다는 뜻이지 해당 지역의 모든 현실 시설이 OSM에 완전히 기록됐다는 뜻은 아니다.

## 수집 방법과 출처

[Geofabrik South Korea](https://download.geofabrik.de/asia/south-korea.html)의 [국가 전체 PBF](https://download.geofabrik.de/asia/south-korea-latest.osm.pbf)를 1회 받아 로컬에서 필터링했다. 원본은 288,583,838바이트, OSM 자료 시각은 **2026-10-06T20:21:06Z**다. 1,103번의 Overpass 조회 대신 한 번의 국가 파일 다운로드로 서버 부하와 반복 수집을 피했다. 다운로드 실패에는 최대 3회, 10초 간격 재시도가 있고 원본이 있으면 재사용한다. 처리 약 291초, 등록된 geometry 파싱 오류 0건이었다.

Pyosmium의 area 조립으로 relation/multipolygon 및 내부 중정을 읽고, Earcut으로 오목한 건물과 중정의 삼각형을 만든다. 데이터는 원본 OSM ID, 이름, 좌표, 형상, 높이·너비의 근거 분류를 유지한다. [OpenStreetMap 기여자, ODbL](https://www.openstreetmap.org/copyright). 배포 시 지도 자료 출처 화면과 동봉 문서에 해당 출처와 라이선스를 유지해야 한다. 추출/변환된 데이터도 ODbL이다.

## 정확도 구분과 빈 자료 처리

| 항목 | 근거 | 한계 |
|---|---|---|
| 도로 중심선·건물 평면 윤곽·출입구 좌표 | OSM에 기록된 형상 | 측량 정확도 보증은 없음. OSM 미등록 시설은 생성하지 않음 |
| 건물 높이 | height 태그 25,536개 | OSM 태그 값이며 이번 작업이 측정한 값이 아님 |
| 층수 기반 높이 | 22,947개, 층당 3m | 시각화 추정 |
| 높이 미기재 건물 | 148,639개, 6m | 시각화 추정 |
| 도로 폭 | width 태그 1,210개 | 차선·보도 실제 단면과 다를 수 있음 |
| 도로 폭 미기재 | 차선 수 기반 9,889개, 기본값 142,597개 | 시각화 추정 |
| 교량 높이 | layer당 5m 또는 최소 5m | layer는 위상 정보이므로 실제 높이가 아님 |
| 실내 대합실·통로·승강장 폭·절대 깊이 | 이 OSM 자료로 확인되지 않음 | 정확한 실내 복원이라고 부르면 안 됨 |

건물 자료가 없는 8개 ID는 검단호수공원, 오수, 반성, 청소, 등구, 김해대학, 계획 광명시흥, 계획 풍양이다. 빠진 건물·출입구·승강장은 임의로 채우지 않는다. underground/tunnel/layer<0/level<0 태그가 있는 도형은 지상 메시에서 제외한다. 관련 태그가 없는 도형은 지상 시각화하므로 실제 고도와 다를 수 있다. 출입구 점은 낮은 비충돌 표식이며 보행 연결은 역 생성기가 별도로 담당한다. 아직 보행 연결이 없는 점을 이용 가능한 출구라고 표시하면 안 된다.

건물 외벽과 지붕에는 기존 공용 반복 텍스처를 적용한다. 외벽 UV는 가로 12m·높이 12.8m 단위이며 실제 창문 위치·상가 간판·입면을 복원한 자료는 아니다. 기존 통합 메시 수와 충돌 형상은 유지한다.

## 공식 실내 치수 보완 자료

[서울교통공사 역사건축정보](https://www.data.go.kr/data/15044258/fileData.do)의 2025-12-31 기준 CSV 276행을 실제 다운로드하여 `StationAreas/metro-architecture-20251231.csv`에 보관했다. 공공데이터 페이지는 승강장 길이(m), 승강장 유형, 역사 층수, 역사 전체 면적(㎡), 준공연도를 명시한다. 이용허락범위 제한 없음으로 표기되어 있다.

실행용 `Geo/StationAreas/architecture.json`과 `StationAreaData.Architecture(station,line)`에서 수도권 1~8호선의 **정확히 일치하는 호선+역명**만 반환한다. 예: 김포공항 5호선 상대식/165m/B3/11,008.53㎡, 강남 2호선 상대식/205m/B2/6,392㎡, 홍대입구 2호선 섬식/205m/B2/7,721㎡. 서울역 1호선 210m, 4호선 205m이다.

공통 철도역 생성기는 일치하는 각 정차역의 공식 승강장 길이를 실제 바닥·지붕 길이에 적용한다. 출입 경사로 쪽 끝을 유지하고 반대쪽으로 늘려 대합실 연결을 보존한다. 공식 길이가 없는 역은 100m, 승강장 폭은 모두 12m의 게임용 추정값이다. 공식 승강장 유형·층수는 자료값으로 화면에 표시하지만, 공통 모델의 한쪽 승강장 배치·출구 위치·층간 깊이가 실제 섬식/상대식 구조나 실내 평면도를 재현한다는 뜻은 아니다.

**역사 전체 면적을 대합실 면적으로 쓰거나 면적÷길이로 승강장 폭을 계산하면 안 된다.** 층수의 B3를 절대 깊이로 변환하는 근거도 없다. 이 자료에는 승강장 폭·천장고·통로별 좌표·실내 전면 평면도·절대 깊이가 없다. 전국 전체 역사에 대한 실측 실내 CAD를 확보했다는 주장 역시 불가하다. 공식 CSV 범위 밖은 미확인 상태를 유지한다.

## 런타임 통합 API

새 파일 `StationAreaData.cs`, `WorldBuilder.StationAreas.cs`.

```csharp
var surroundings = BuildStationArea(station, origin, northRotation, excludedWorldBounds, 450f);
var architecture = StationAreaData.Architecture(station, line);
```

- 좌표 기준: 회전 전 동쪽 +X, 북쪽 +Z. origin은 해당 역의 지표 중심, northRotation은 Y축 회전이다. 역 방향을 바꾸면 주변 지형에도 같은 변환을 적용해야 한다.
- exclusion은 월드 Bounds 배열. 건물 AABB가 만나는 경우 건물을 생략하고 도로·보도 바닥 삼각형은 제외영역에서 잘라낸다. 계단구멍을 포장면이 덮지 않게 이 배열을 전달해야 한다.
- `WorldBuilder.NetworkStation.cs`가 철도역 생성 후 `RefreshNetworkArea`로 주변 자료를 생성하며, `GameController.NetworkStation.cs`는 현재 정차역이 바뀌면 해당 역 중심으로 주변 메시를 교체한다. 현재 공통 역 장면은 지도에 Y축 180도 회전을 적용한다. 이것이 실제 선로 방향과 공통 선로 배치가 일치한다는 뜻은 아니다.
- 지하/지상/고가 구분은 기존 OSM 선로 자료에서 추정한다. 공통 모델의 경사로·대합실·방향별 출구는 연결되어 있지만, OSM 출입구 점은 실제 좌표에 놓인 비충돌 표식일 뿐 자동으로 보행 가능한 출구와 연결되지 않는다. 수집된 지도 출입구 전체를 이용 가능한 출구로 안내하면 안 된다.
- 지형은 최대 8개 종류별 통합 메시이며 건물과 보도 충돌도 통합한다. 매 건물마다 GameObject/Collider를 만드는 방식이 아니다.
- 압축 타일은 `Resources.Load<TextAsset>`으로 필요한 것만 읽고 TextAsset은 바로 unload한다. 해석된 타일 LRU는 최대 12개다.
- 생성된 Mesh는 기존 `AccessMeshOwner`로 해제된다. 큰 통합 메시의 실제 CPU/GPU 프레임 시간은 게임 실행에서 별도로 점검해야 한다.
- 이용자가 이동시킨 역(기존 ID 좌표와 5m 이상 다름)이나 새 사용자 역은 기존 위치의 자료를 잘못 붙이지 않고 자료 없음으로 반환한다.

## 검증 및 재생성

`validate_station_areas.py`로 전체 2,911개 타일을 읽어 중복 ID, 비정상 좌표, 삼각형 범위, 누락된 manifest 타일을 검사: 오류 0건. 2,253,661개 2D 점, 한 도형 최대 734점, 한 타일 최대 2,217개 도형. 상세는 `validation.json`.

전체 런타임·Editor C# 소스의 Roslyn 컴파일에 성공했다. 이어 2026-10-08 통합 실행에서 Unity 자동 검사와 macOS 0.9.7 빌드가 통과했다.

- `StationAreaCheck`: 실제 Unity 로더로 1,103개 역 ID, 2,911개 타일, 365,196개 도형 해석을 확인했다. 강남·홍대입구·김포공항의 공통 철도역을 생성해 주변 메시 충돌, 공식 승강장 길이(205m·205m·165m), 경사로 연결, 다음 역 주변 갱신을 검사했다. 로그: `/tmp/stationarea-097.log`.
- `InternationalAirportCheck`: 양방향 경로 17개, 무빙워크 16개를 검사했다. 생성 규모는 렌더러 237개·오브젝트 271개다. `AirportAccessCheck`, `GimpoLayoutCheck`(양방향 경로 23개·탑승 방향 10개·보행 모터 이동 약 11.7km), `StationClearanceCheck`, `WalkAccessCheck`, `ExpansionCheck`도 통과했다. 실행 순서는 `work/097/check_build.py`, 로그는 `/tmp/*-097.log`에 있다.
- macOS 빌드 결과 `Succeeded`, 오류 0건. 로그: `/tmp/peninsula-mac-097.log`.

전국 모든 역의 자료 해석과 위 세 역의 생성·충돌 검사를 구분해야 한다. 이 자동 검사 통과가 1,103개 역을 모두 직접 돌아다닌 결과나 전국 실내의 실측 복원 정확도 검증을 뜻하지는 않는다.

```sh
python3 -m venv AssetSources/StationAreas/.venv
AssetSources/StationAreas/.venv/bin/pip install -r AssetSources/StationAreas/requirements.txt
AssetSources/StationAreas/.venv/bin/python AssetSources/StationAreas/build_station_areas.py --download
python3 AssetSources/StationAreas/import_architecture.py
AssetSources/StationAreas/.venv/bin/python AssetSources/StationAreas/validate_station_areas.py
```

캐시 원본은 `AssetSources/StationAreas/cache/`에만 있고 Resources에 복사하지 않는다. `cache/`, `.venv/`, `compile/`은 배포 대상이 아니다.
