# 0.9.7 station area API (integrated)

Integrated in `WorldBuilder.NetworkStation.cs` and `GameController.NetworkStation.cs`.

`public StationAreaBuildResult BuildStationArea(NetStation station, Vector3 worldOrigin, Quaternion eastNorthRotation, IList<Bounds> excludedWorldBounds = null, float radius = 450f)`

Call after `root` is created. Geographic east maps to local +X, geographic north to local +Z. `eastNorthRotation` rotates both about station center `worldOrigin`; provide Quaternion.identity if the local station scene remains geographic. Exclusions are WORLD axis-aligned Bounds; building polygons intersecting exclusions are omitted and road/footpath segments are clipped to exclusions. Underground-tagged platforms are omitted from street rendering. An absent underground tag is not proof of surveyed elevation; surface heights remain visual estimates. OSM entrance points are nonblocking coordinate markers. They are not automatically connected to the playable station and must not be presented as usable entrances.

`BuildNetworkStation` now calls `RefreshNetworkArea` after constructing the common railway station. `UpdateNetworkJourney` refreshes those surrounding meshes at the new stop center whenever the current stop changes. The common scene currently uses a 180-degree Y rotation for its map; the common track layout is not a reconstruction of the surveyed railway alignment. Station access exclusions protect the generated walking routes from mapped buildings and ground surfaces. The station panel reports the data source and loaded feature counts.

Data: one cached Geofabrik South Korea PBF (about 275 MB), filtered offline to all game visitable rail station radii. This avoids repeated Overpass queries. Runtime loads only nearby preprocessed tiles through Resources, with bounded tile cache. Dimensions without OSM tags use explicitly documented visualization defaults; no claim of surveyed interiors.

`StationAreaData.ForStation(station)` returns a manifest entry (null if custom/uncovered or moved at least 5m from the recorded coordinates). Each entry has counts and tile keys. The renderer result reports rendered objects / exclusions / missing data. Nationwide collection completed: 1,103/1,103 rail station IDs have real surroundings, 1,103 roads, 1,095 buildings, 937 entrance coverage, 402 platform coverage; missing feature classes remain missing. These are game station IDs, including interchange duplicates and planned scenarios, not a claim to cover every real Korean station. There are 2,911 compressed tiles, 22,994,834 runtime bytes before the architecture data, source timestamp 2026-10-06T20:21:06Z.

Official interior metrics now available: `StationAreaData.Architecture(station, line)` returns `platformLength`, `platformType`, `floorLabel`, `totalStationArea` for exact line+name matches in 276 official Seoul Metro rows. Do not derive platform width or absolute depth from totalStationArea/floorLabel. Gimpo L5 length 165m / side platforms / B3; Gangnam L2 205m / side / B2; Hongdae L2 205m / island / B2. Dataset and caveats: AssetSources/STATION_AREAS_097.md.

`NetworkPlatformLength` applies matching official lengths to each stop's platform and roof. The access end stays at stop-center Z minus 50m; the far end extends to preserve the ramp and concourse connection. Missing lengths fall back to 100m. The 12m width is assumed, and the common model's one-sided platform arrangement, exits and floor depths remain schematic. Official type/floor labels are source metadata, not a claim that the generated arrangement matches those details.

## Verification completed 2026-10-08

Runtime and Editor Roslyn compilation passed. The integration runner then passed `StationAreaCheck` in Unity: all 1,103 station IDs / 2,911 gzip tiles / 365,196 features decoded; Gangnam, Hongdae and Gimpo common stations generated with mapped mesh collisions, official platform lengths, continuous ramps and next-stop surroundings refresh checked. See `/tmp/stationarea-097.log`.

The same sequential run passed `InternationalAirportCheck` (17 bidirectional paths, 16 moving walkways, 237 renderers / 271 objects), `AirportAccessCheck`, `GimpoLayoutCheck` (23 bidirectional routes, 10 boarding sides, about 11.7km of character-motor movement), `StationClearanceCheck`, `WalkAccessCheck`, and `ExpansionCheck`. macOS 0.9.7 build succeeded with zero errors (`/tmp/peninsula-mac-097.log`). Runner: `work/097/check_build.py`.

Full dataset decoding and three generated-station geometry checks are distinct from a manual walkthrough of all 1,103 station IDs. Automated checks do not establish surveyed interior accuracy, complete OSM coverage, or actual walking connectivity at every mapped entrance marker.
