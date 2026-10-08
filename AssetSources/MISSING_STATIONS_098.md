# 0.9.8 건물 자료 누락 8곳 조사·보완

## 적용 결과

2026-10-08, 기존 1,103개 게임 역 ID 중 OSM 건물 윤곽이 0개인 8곳을 각각 검색하고 현재 위치·이전 역·계획역 여부를 확인했다. 현재 역 좌표를 옮길 근거는 없었다. Overture Maps 공개 건물 윤곽 **417개 폴리곤 조각**을 6곳에 보완했다. 김해대학의 한 다중 폴리곤은 2개 조각으로 저장된다.

| 역 | 게임 ID | 경도, 위도 | 추가 건물 윤곽 | 결과 |
|---|---|---|---:|---|
| 검단호수공원 | r12961370708 | 126.688545, 37.602441 | 5 | 공개 윤곽 보완; 신도시 공사 이후 변화는 미확인 |
| 오수 | r368637440 | 127.320403, 35.543082 | 0 | 공개 윤곽 없음; 사진 참고 외관 3부품 별도 제작 |
| 반성 | r4691566557 | 128.258244, 35.174811 | 0 | 공개 윤곽 없음; 사진 참고 외관 4부품 별도 제작 |
| 청소 | r4753904292 | 126.590776, 36.445678 | 72 | 공개 윤곽 보완 |
| 등구 | r5197043949 | 128.963504, 35.196205 | 110 | 공개 윤곽 보완 |
| 김해대학 | r5208433886 | 128.915571, 35.228980 | 224 | 공개 윤곽 보완 |
| 광명시흥 | scenario-광명시흥 | 126.856000, 37.439000 | 2 | 계획 시나리오 중심 주변 기존 건물; 미래 역사 복원 아님 |
| 풍양 | scenario-풍양 | 127.151000, 37.721000 | 4 | 계획 시나리오 중심 주변 기존 건물; 미래 역사 복원 아님 |

건물 자료가 있는 역 ID는 **1,095→1,101개**, 전체 건물 도형은 **197,122→197,539개**다. 기존 2,911개 타일 중 14개를 갱신했고 새 타일은 없다. 김해대학에서 보완한 도형 중 20개는 인접한 지내역 반경에도 들어가므로 지내역에서도 동일 ID의 도형이 표시된다. 서로 다른 20개를 추가 생성한 것이 아니다. 오수 주변 도로 16개·선로 12개, 반성 주변 도로 32개·선로 11개는 원래의 실제 OSM 자료를 유지한다. 두 역에 측정하지 않은 건물 윤곽을 실제 지도 자료인 것처럼 넣지 않았다. **8곳 전체 정밀 복원 완료가 아니다.**

위 공개 도형 보완에 더해, 사용자가 8곳 모두 제작하도록 요청한 범위에 따라 오수·반성에는 **사진 참고 추정 외관 2세트·7부품**을 별도로 제작했다. 이 외관까지 포함하면 1,103개 역 ID 모두 건물 표현을 갖는다. 공개 건물 윤곽이 있는 역의 수는 여전히 **1,101개**다. 최종 건물 도형·부품 합계는 197,546개이며, 7부품을 실제 건물 7채로 세면 안 된다.

## 오수·반성의 사진 참고 추정 외관

두 Wikimedia Commons 사진을 브라우저로 열고 직접 관찰한 뒤 제작했다. 사진 원본이나 잘라낸 텍스처를 게임에 포함하지 않았다. 게임 형상은 새로 작성한 단순 입체이며, 실제 측량 자료와 분리하여 `authored098:` ID와 `heightSource=authored_photo_estimate`, `footprintSource=authored_photo_reference_approximation`을 기록한다.

| 역 | 사진 관찰 | 제작 내용 | 확인되지 않은 부분 |
|---|---|---|---|
| 오수 | G43, 2009-08-13, CC BY 3.0. 선로 쪽 회색 평지붕, 높은 중앙부, 낮은 양쪽 부속동 | 중앙부 18×22m·높이 8.2m와 양쪽 16×16m·높이 4.2m의 3부품 | 모든 선택 치수·정확한 중심점·창 배치·실내·이후 개보수 |
| 반성 | 안우석, 2012-12-17, public domain. 긴 회색 입면, 앞쪽의 높은 입구 프레임 | 50×14m·높이 6.2m 본체와 높이 11m 프레임 3부품 | 모든 선택 치수·정확한 배치·프레임 경사·실내·현재 외관 |

**치수는 제작자가 선택한 추정값이다.** 오수 외관은 역 중심 동쪽 55m·남쪽 32m, 반성은 동쪽 62m·남쪽 18m의 공통 역 장면 주변에 도식적으로 배치했다. 실제 역사 CAD 좌표가 아니다. 기존 플레이 가능한 선로·출구를 막지 않고 역 주변에 외관이 보이게 하기 위한 게임용 배치다. 이 형상은 출입 가능한 실내를 추가한 것이 아니다. 공통 역의 보행·환승 기능과 별개인 외관 표현이다.

`authored-stations-098.json`에 원본 사진 URL·저작자·촬영일·라이선스·관찰 내용·선택한 치수·배치·불확실성을 보관한다. [반성역 사진](https://commons.wikimedia.org/wiki/File:Banseong-O1.JPG)의 위치 정보 자체도 사후 추정으로 안내되어 있다. 오수 외관의 참고사진 표기는 **G43 / CC BY 3.0 / 사진 참고 단순화**를 유지한다. 반성 참고사진은 안우석의 public domain 사진이다.

반성 프레임은 `subtype=authored_station_portal`로 분리되어 있다. 이 데이터는 형태·높이를 제공하며, 공통 렌더러의 회색 외벽 재질을 기본으로 사용한다. 사진의 붉은색까지 표시하려면 런타임에서 해당 subtype에 붉은 재질을 적용한다.

## 역별 확인 자료

- **검단호수공원:** [인천교통공사 공식 역 안내](https://www.ictr.or.kr/main/subway/subwayStation.do?line_no=1&station_no=107). I107, 불로동 608-16, B1 대합실/B2 승강장, 상대식, 반대 방향 횡단 불가. 안내에 없는 통로 치수는 확인하지 못했다.
- **오수:** [임실군지 제5권](https://www.imsil.go.kr/images/01_potal/file/sub05/imsil_gunji05.pdf), 176–177쪽·502–503쪽. 충효로 1967-19의 현 역사와 삼일로 56의 옛 역사를 구분했다. [현재 역사 후면의 위치 정보가 있는 사진](https://commons.wikimedia.org/wiki/File:Korail_Jeolla_Line_Osu_Station_Rearside.jpg)도 대조했다. 사진으로 실제 건물의 존재는 확인되지만 정확한 평면·층고·실내 치수를 얻은 것은 아니다.
- **반성:** [관보 제19913호](https://upload.wikimedia.org/wikipedia/commons/c/cb/%EA%B4%80%EB%B3%B4_%EC%A0%9C19913%ED%98%B8_%EC%9D%B8%EC%82%AC.pdf)의 주소 자료, [진주시의 옛 반성역 활용에 관한 보도](https://www.newsjinju.kr/news/articleView.html?idxno=18716), [현 역사 외관 사진](https://commons.wikimedia.org/wiki/File:Banseong-O1.JPG)을 대조했다. 현재 일반성면 일사로 787의 역사와 이전 부지를 혼동하지 않는다. 사진만으로 측량 평면을 만들지 않았다.
- **청소:** [법무부 공존 2022년 61호](https://www.moj.go.kr/sites/immigration/file/ebook/gongzone/2022/2022_gongzone_vol61.pdf)의 청소역 소개와 [보령시 해양항만 발전 기본계획](https://clik.nanet.go.kr/clikr-collection/policyinfo/61/802/2017/CLIKC344601959976196_attach_1.pdf)의 문화유산 자료. 청소큰길 176의 역사 위치를 확인했다.
- **등구:** [부산김해경전철 공식 안내](https://www.bglrt.com/00011/00149.web?scode=906). 부산 강서구 대저2동 3133-7, 지상↔대합실↔승강장 승강기 안내와 출구 버스 연계를 확인했다.
- **김해대학:** [부산김해경전철 공식 안내](https://www.bglrt.com/00011/00149.web?scode=912). 김해대로 2641, 실외 승강장, 반대 방향 이동 가능. 역 이름만으로 김해대학교 캠퍼스 위치에 역을 옮기지 않는다.
- **광명시흥:** [광명시 공식 GTX-D 제안 관련 자료](https://news.gm.go.kr/bbs/view.html?idxno=3975) 및 [2026년 정책 자료](https://www.gm.go.kr/pd/psw/PDPSW_12000/PDPSW_12000_2026.jsp). 가칭 계획역이다. 현재 게임의 `approximate`·`plannedOnly` 상태를 유지했다.
- **풍양:** [남양주시 왕숙 공공주택지구 안내](https://nyj.go.kr/industry/contents.do?key=4959)의 계획도와 [진접2 지구 공급 안내](https://www.jj2-b1.co.kr/resources/upload/catalogue.pdf)를 대조했다. 계획도는 확정 실측 역사 배치도가 아니다. 기존 계획 시나리오 상태를 유지했다.

위 역사 안내 자료는 위치·시설 조건 확인용이다. 이번 변경은 주변 건물 타일 데이터이며, 각 역사 안내의 내부 구조를 전부 구현했다는 뜻이 아니다.

## 공개 윤곽 출처와 정확도

[Overture Maps 클라우드 데이터](https://docs.overturemaps.org/getting-data/cloud-sources/)의 **2026-09-23.1** 릴리스를 고정했다. 8곳의 반경 450m와 교차하는 도형만 추출한다. 전체 국가 파일을 새로 받지 않는다. 새 건물 417개의 upstream은 모두 [Qian Shi 외, East Asian Buildings](https://zenodo.org/records/8174931), DOI `10.5281/zenodo.8174931`이다. 원 자료는 2023년 발행되었고 Overture의 해당 소스 레코드는 2024-11-01 버전으로 표시되어 있다. **2026년 Overture 배포일이 모든 건물의 2026년 현황 검증일을 뜻하지 않는다.**

- 공개된 지리 데이터의 건물 평면 윤곽을 사용한다. 측량 정확도는 보증하지 않는다.
- 새 417개에는 높이·층수 값이 없어 모두 기존 규칙의 **6m 추정 높이**를 사용한다. 현장 측정값이 아니다.
- 외벽·창문·간판·내부 CAD·출입문 좌표는 확보하지 않았다. 기존 공용 표현을 사용한다.
- 기존 OSM 도형이 있는 대상 역에는 추가 소스를 중첩하지 않는다. 같은 보완을 다시 실행하면 `ovt:` 도형만 교체한다.
- 지하 도형 처리·삼각분할·중정·경계 선택은 기존 `Extract` 변환을 사용한다. 별도 GameObject 생성 코드는 추가하지 않았다.

라이선스는 Overture 건물 데이터 ODbL, upstream East Asian Buildings CC BY 4.0이다. [Overture 공식 출처·라이선스 안내](https://docs.overturemaps.org/attribution/). 배포 동봉 문서와 지도 출처 화면에 다음 출처를 유지한다.

> © OpenStreetMap contributors; Overture Maps Foundation; Qian Shi et al. (2023), A First High-quality Vector Data of Buildings in East Asian Countries Based on a Comprehensive Large-scale Mapping Framework, doi:10.5281/zenodo.8174931 (CC BY 4.0).

추가로 검색한 Microsoft Global ML Building Footprints 배포본에는 해당 한국 타일이 없었다. 최신 국내 행정안전부 건물 레이어를 재제공하는 Esri 서비스는 오프라인 재배포 허락을 확인하지 못해 게임에 복사하지 않았다.

## 재현·검증

```sh
AssetSources/StationAreas/.venv/bin/pip install -r AssetSources/StationAreas/requirements-supplement.txt
AssetSources/StationAreas/.venv/bin/python AssetSources/StationAreas/supplement_missing_areas.py
AssetSources/StationAreas/.venv/bin/python AssetSources/StationAreas/build_authored_station_exteriors.py
AssetSources/StationAreas/.venv/bin/python AssetSources/StationAreas/validate_station_areas.py
```

기본 OSM 자료를 재생성한 뒤 공개 윤곽 보완, 사진 참고 외관 보완 순서로 실행한다. 기본 실행은 원본 캐시를 재사용하며 `--refresh`는 고정된 릴리스에서 원본을 다시 읽는다. 사진 참고 외관은 나중에 공개 건물 윤곽이 확보되면 그 역에 더 이상 넣지 않는다. 캐시·가상환경은 배포에 포함하지 않는다.

- 원본 필터 결과 SHA256: `125de651ec68f24d477efb93b24746ec7ec8537e3cc53ea1b68ae09c8de7955f`.
- `StationAreas/supplement-098.json`: 417개 ID별 원 출처·버전·높이 추정 여부와 역별 결과.
- `StationAreas/coverage.json`: 원래 OSM 수집 기록을 보존하고 `supplement098`에 보완 후 합계를 기록.
- `StationAreas/authored-098-receipt.json`: 사진 참고 외관 7부품의 치수·참고 사진·불확실성과 원본 정의 SHA256.
- `StationAreas/coverage.json`의 `authored098`: 공개 윤곽 보유 수와 사진 참고 표현을 포함한 수를 분리한다.
- 공개 윤곽 보완 단계: 1,103개 역·2,911개 타일·2,256,065개 점, **오류 0건**. 사진 참고 부품을 포함한 최종 검사 역시 오류 0건이며, 2,256,093개 점을 검사했다. `StationAreas/validation.json`에 공개 윤곽 부재 2곳과 외관 표현 보유 1,103곳을 구분해 기록한다.
- 보완 스크립트 재실행 전후 모든 타일 파일 SHA256이 같았다. 417개가 중복해서 늘어나지 않는다.
- 데이터 검증만 수행했다. 이 작업의 검증을 실제 플레이·프레임 시간·역 내부 정밀도 검증으로 표현하지 않는다.
