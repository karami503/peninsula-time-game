# Gimpo station research — 2026-10-08 (research complete; approximate geometry marked)

## Critical coordinates

Fresh Overpass result saved at `AssetSources/OSM/gimpo-station-096.json`. Existing `gimpo-osm.xml` has no station or entrance nodes: its terminal-centred extraction stopped before these features. Do not reuse default origin as station centre.

Projection **must match extract_seoul_features.py**, NOT national GeoProjection: `x = -(lon-126.8025)*111320*cos(37.5588°)`, `z=-(lat-37.5588)*111320`. Thus +x west, +z south. Units metres.

| Exit | OSM node | Latitude | Longitude | world x | world z |
|---|---|---|---|---|---|
|1|3396415905|37.5615202|126.8008731|143.57|-302.81|
|2|3396415903|37.5634922|126.8011290|120.99|-522.34|
|3|3396415904|37.5625739|126.8018222|59.81|-420.11|
|4|3396415906|37.5615840|126.8021015|35.17|-309.91|

Fresh station point references (these are map label nodes, NOT surveyed centre of structure):

| Node / operator | lat | lon | x | z |
|---|---|---|---|---|
|5772497241 / Line5|37.5619247|126.8013934|97.65|-347.84|
|10814394660 / Goldline|37.5623275|126.8019608|47.58|-392.68|
|10814394661 / Line9|37.5619877|126.8015843|80.81|-354.85|
|10814394662 / AREX|37.5617450|126.8015857|80.68|-327.84|
|10814394663 / Seohae|37.5617187|126.8040606|-137.72|-324.91|

## Floor structure — confirmed by user-supplied station evacuation drawing

- 5호선: B1/B2 concourses, B3 platforms, two sides of two centre tracks.
- 9호선+AREX: shared island platforms stacked at B3/B4 (NOT separate 9/AREX islands). B3 = AREX Seoul + 9 eastbound 중앙보훈병원; B4 = AREX Incheon + 9 westbound 개화.
- Goldline: B2/B3 concourse, B4 platforms (relative side platforms).
- Seohae: B2/B4 concourse, B5 platforms (relative side platforms).
- Secondary sources report Seohae depth 83m (not independently confirmed here); B5 does not mean ordinary 5*6m storeys. Treat unmeasured heights explicitly, use long escalator/vertical circulation if modelling depth.
- All east/west route lines in the new user map are station **lengths**, not a giant shared rectangular hall. 5호선 body north-south / slight curve, 9+AREX body west-east, Gold body west-east north of9, Seohae body north-south / diagonal east ofthe others.

## Alignment from OSM + supplied map (inference; length/width not surveyed)

- 5 axis near world (100,-415), +Z yaw roughly -5 to -12deg: north endpoint near (103,-497), south near (92,-300). User purple body extends ~150m (about yimage470..700 at scale50m/80px), axes should follow slight curve. Choose ~150–180m body, ~20m width.
- Shared 9/AREX axis ~world (−15,-345), toward east = x decreasing and z increasing. +Z oriented yaw about−86deg. OSM 9 polyline (85.03,-353.14)→(-61.02,-343.46); body map extends farther east to nearSeohae, approximately250m length.
- Goldline body north of9, roughly (−10,-390), yaw−86deg, ~240m body. Its maplabelnode(47,-393) is not structure centre.
- Seohae body near (−139,-323), north-south diagonal. User screenshot line slopes right as goes down -> +Z direction has x negative, yaw~-10deg. Body~200m. Derive more exact angle from fresh rail ways if needed.

## Sources/status

- User attached Naver satellite image, exit circles and footprint coloured blocks (2026-10-08 request): primary placement/layout target.
- User attached station emergency/evacuation diagram (earlier request): primary floor/topology target.
- OpenStreetMap raw Overpass query fetched2026-10-08; https://www.openstreetmap.org/node/3396415905 etc. ODbL.
- Official Metro9 station page: https://www.metro9.co.kr/prog/subwayInfo/kor/sub01_01/view.do ; discovered Gimpo sbwyNo4102. Official AJAX station record verified with sbwyNo=4102. It states island platform, B4, four exits. Official guide image retrieved and visually inspected: https://www.metro9.co.kr/thumbnail/subwayInfo/920_SI_20240216175050730Li71.jpg . The guide shows stacked shared 9/AREX platforms and separate Goldline wing, but omits the later Seohae wing; use the user supplied newer composite evacuation image for Seohae.
- NamuWiki direct URL https://namu.wiki/w/%EA%B9%80%ED%8F%AC%EA%B3%B5%ED%95%AD%EC%97%AD could not be opened by web tool. User supplied NamuWiki screenshots remain usable references. Search-indexed mirror supplied floor consistency check, not sole authoritative source.

No runtime code edited, no Unity runs.

## Official exit destinations and vertical circulation

Metro9 official station record (`POST https://www.metro9.co.kr/prog/subwayInfo/kor/sub01_01/infoAjax.do`, `sbwyNo=4102&tabSe=01`):

|Exit|Official destinations|
|---|---|
|1|한국공항공사, 국내선청사|
|2|국제선주차장, 국제선청사|
|3|롯데몰(김포공항)|
|4|국내선주차장|

Image alternative text specifies lift from exit 3 to B2; exit 4 approach reaches B1 by escalator; lift connection between B1 and B3/B4. Official station address 서울 강서구 하늘길 지하77. Facilities list 3 lifts and 18 escalators (operator scope; do not count as whole five-line complex).

Implementation consequence: anchor surface doors at the exit coordinate table; use separate connecting passage branches to concourses, with long corridors receiving moving walkways as requested. Do not translate a whole station to a terminal spawn point or rotate every line identically. References describe topology; heights, widths, exact curves and metric platform extents still require engineering plans and are explicitly approximations.
