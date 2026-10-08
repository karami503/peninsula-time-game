# 김포공항 국제선 청사 — 0.9.7

## Integrated implementation

- New partial: `WorldBuilder.InternationalAirport.cs`.
- Gimpo airport construction calls public, idempotent `BuildInternationalAirport()` from `WorldBuilder.Airport.cs`, after domestic terminal creation. The existing domestic terminal is retained.
- Game annex `InternationalAirportOrigin=(900,0,-800)`; source orientation is rotated into the game's straight axes. This is not the real georeferenced terminal location.
- B1 network branch `InternationalAirportConnector=(440,-5.8,-496)` and initial direction **-Z**. `LinkGimpoExits` now adds an 8 m mouth toward -Z before generating the existing passage union. The international corridor begins here with an open end and a 6 m side opening; actual traversal across this join passed the walking-motor check.
- Public 1F arrival spawn `InternationalArrivalSpawn`; all route point arrays are feet coordinates (`InternationalWalkRoute`, separate from domestic/station routes).
- Public floor heights 0,6,12,18 m; B1 -5.8 m. Raised entrance openings/stairs are generated together with their floor cutouts.
- Editor validation entry: `PeninsulaTime.InternationalAirportCheck.Run`. The integrated Unity check has passed.

## Sources read directly, 2026-10-08

1. KAC official [피난안내도](https://www.airport.co.kr/gimpo/cms/frCon/index.do?MENU_ID=2600), with all four international floor SVGs downloaded and visually inspected:
   - https://www.airport.co.kr/type/airportKor/img/shelter/gimpo_international_1F.svg
   - https://www.airport.co.kr/type/airportKor/img/shelter/gimpo_international_2F.svg
   - https://www.airport.co.kr/type/airportKor/img/shelter/gimpo_international_3F.svg
   - https://www.airport.co.kr/type/airportKor/img/shelter/gimpo_international_4F.svg
2. KAC official [시설안내](https://www.airport.co.kr/gimpo/cms/frFacCon/facilityMapList.do?MENU_ID=1420&acd=A1101&listGbn=2). Public facility API lists international floors 1F,2F,3F,4F with floor IDs 5,6,7,8.
3. KAC official [국제선 출발시뮬레이션](https://www.airport.co.kr/gimpo/cms/frCon/index.do?CONTENTS_NO=1&MENU_ID=1100).
4. [OpenStreetMap way 226573240](https://www.openstreetmap.org/way/226573240), downloaded from public OSM map API. Named 김포국제공항 국제선청사 / Gimpo International Airport International Terminal, tagged aeroway=terminal/building=yes. WGS84 bounds latitude 37.5640561–37.5680733, longitude 126.7981642–126.8032276. A locally projected minimum bounding rectangle is approximately **613.46 × 131.34 m** (latitude-dependent metre conversion, not survey precision). This includes the full tagged outline/pier projections, not four identical full-size floorplates. OSM data © OpenStreetMap contributors, ODbL.

Raw research is saved under `work/097/`. Runtime uses no remote queries or downloaded SVG textures.

## Confirmed spatial references

- 1F: arrivals entrance from airside into public arrival hall, central projecting forehall, public entrances 1–3 and parking-side connections.
- 2F: central public check-in forehall and self check-in; stairs around either side; long side wing.
- 3F: central departure entrance, airside hall and long gate pier; labels 34,35,R1,36,37,38,39 appear on the official plan.
- 4F: upper-floor hall with a central notch/open area and side stairs. Facility functions are checked separately from the evacuation drawing.

## Game assumptions and limits

- This is a walkable interpretation of public maps, not a surveyed architectural reconstruction or an operational airport simulator.
- Annex location/rotation, 6 m storey spacing, exact wall offsets, stair run/width, counter/furniture dimensions and barrier positions are game assumptions. The approximate total exterior scale follows the OSM outline; floor areas follow simplified public-plan shapes.
- Security and immigration are labelled spatial zones. No real passenger credentials are collected, and this file does not add international flights or alter domestic tickets.

## Official facilities cross-check

The facility guide's public `getFloorObjectInfo.do` results were saved as `work/097/kac-floor-5.json` through `kac-floor-8.json`. The 4F response confirms restaurant category plus **하늘찬(한식당)** and **대청마루**. The 1F response includes the arrival duty-free shop and the 3F response includes **신한은행 환전소(3층 출국장)**. These support the floor functions; commercial tenants have not been copied as a complete shop inventory. Runtime signs use generic facility names, so store tenancy changes do not pretend to be current.

## Implemented interpretation

- Overall 614 m terminal/pier span and 130 m main depth; 320 m broad central hall, side wings, 4F forehall notch. Four storeys have assumed 6 m spacing.
- 1F: three public doors, arrivals hall, baggage collection, public side passage from B1. A partition keeps the side access route clear of baggage fixtures.
- 2F: four public doors to a guarded outside balcony, four check-in counter groups, self check-in, waiting areas.
- 3F: central open security/search lanes, departure inspection zone, duty-free counters, gate labels 34–39 and R1. Two opposing moving walkways serve the long eastern pier.
- 4F: restaurant/café counters, seating and the guarded central notch.
- Two side stairwells connect all floors. Return flights use separate adjacent lanes, so their meshes cannot cross above the incoming landing. Each upper floor has the matching physical hole, and every flight/landing is recorded for forward and reverse motor tests.
- A 7.2 m wide B1 corridor connects the existing station/domestic branch to international 1F with rounded turns, continuous floor and ceiling, opposing moving walkways and a final enclosed stair flight.
- New generation directly batches box/quad vertices by material and 128 × 192 m spatial cells, aligned so one hall is not needlessly split at its centre. No individual object is instantiated for every stair tread, rail section, seat or check-in desk. Emissive ceiling fixtures add zero extra point lights or per-frame animation scripts; only the small MovingWalkway sensors remain active.

## Integration and lighting

- `BuildInternationalAirport()` is public and idempotent.
- The call is integrated into Gimpo airport construction after the domestic terminal is created.
- `IsInternationalAirportInterior(Vector3 eye)` includes terminal volumes, piers and B1 passage; `IsDomesticAirportInterior(Vector3 eye)` includes the existing hall and elevated gate wings. `WorldBuilder` lighting and `GameController.Minimap` now use these predicates. Neither marks the runway or outdoor sky as indoor merely because x is large.
- `InternationalArrivalSpawn`, `InternationalCheckInSpawn`, `InternationalDepartureSpawn` expose eye positions. `InternationalAirportRoot` exposes the generation root.
- The station branch mouth is integrated as described above, and the actual station-to-international seam traversal has passed.

## Verification status — 0.9.7

- Runtime and Editor C# compiler checks passed (existing unrelated GimpoLayoutCheck reference-comparison warnings remain).
- **Unity `PeninsulaTime.InternationalAirportCheck.Run` passed:** 17 bidirectional route records, 16 functioning moving walkways, **237 renderers / 271 objects**. The check includes a continuous entrance→check-in→security→gate39 walk, the existing B1 seam, idle-passenger carrying, stair headroom, 4F notch fall prevention, indoor/outdoor lighting predicates, renderer/object budgets and idempotent construction. Evidence: `/tmp/internationalairport-097.log`.
- **Domestic regression check passed:** all eight aircraft bridges/cabins, three floor connections, metro→security and 24 forward-overshoot/cabin-recovery walks. Evidence: `/tmp/airportaccess-097.log`.
- **Gimpo station regression check passed:** 23 bidirectional routes, 10 actual train-boarding sides and 11,739.5 m of movement through the walking motor. Evidence: `/tmp/gimpolayout-097.log`.
- The coordinating agent completed the **macOS 0.9.7 build successfully**.
- **Final macOS 0.9.7 visual playtest completed:** entered the international stairwell, walked the check-in/departure halls and rode the eastern moving walkway. A seat overlap found during play was corrected and the final build was visually rechecked. Automated walking checks cover all recorded paths; manual play did not visit every part. On Apple M5 at 2704 × 1670, balanced settings, the international check-in and gates camera samples each averaged 59.7 FPS (4-second warm-up, 6-second sample). Real-world dimensions and functional limitations remain as stated above; this implementation does not add international flight services.

### Initial failures resolved

The first Unity run reported 20 failures: one 4F route crossed a seat, the new B1 side wall blocked the station seam, sixteen EditMode moving-walkway sensors were not registered, and the renderer/object budgets were exceeded (326 renderers / 360 objects). Corrections moved the route into the table aisles, opened the first six metres of the new corridor sides, explicitly registered the carriers and aligned material bins to 128 × 192 m cells. The rerun passed all checks with the original budget assertions unchanged (<260 renderers, <320 objects), measuring 237 renderers / 271 objects. This is 89 fewer renderers and 89 fewer objects than the initial implementation, without claiming a frame-rate measurement.

## Final playtest correction

macOS에서 동측 무빙워크 난간 안쪽으로 좌석 끝이 일부 겹친 것을 직접 확인해 동측 좌석 두 줄을 z=34.5, 37.5로 옮겼다. 중앙 양쪽 0.3m 위치의 경로를 각각 왕복하는 8개 검사(반경 0.28m 캡슐 충돌 및 실제 보행 모터)를 추가한 뒤 InternationalAirportCheck가 통과했다. 최종 렌더러 237개, 오브젝트 271개로 기존 예산도 유지한다.
