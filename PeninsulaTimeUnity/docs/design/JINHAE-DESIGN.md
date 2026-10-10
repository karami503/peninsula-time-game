# PeninsulaTime Jinhae design system

## Visual source

The canonical style is the walkable Jinhae district and the reconstructed 1926 Jinhae station. Public spaces use warm plaster, granite and timber against cool slate and muted blue glass. Yellow is reserved for tactile paving, wayfinding and player targets.

## Surface rules

| Role | Token | Use |
|---|---|---|
| Main walls | Cream | Station halls, airport halls, public buildings |
| Base and hard landscape | Granite / Concrete | Platforms, bridges, tunnels, kerbs, stairs |
| Human touch surfaces | Timber | Benches, counters, heritage trim |
| Roof and road darks | Slate / Shingle | Roofs, asphalt and structural shadow |
| Windows | Pane | Muted blue glass with transparency where supported |
| Metalwork | Aluminium | Frames, rails, mullions and vehicle trim |
| Guidance | Tactile | Tactile paths, destinations and selected controls |

Transit line colours, traffic signals, warnings and emitted lights keep their original colours because they convey operational information.

## Shape and layout rules

- Public routes remain continuous from street entrance to concourse and platform.
- Station halls keep clear sight lines and use signs above the walking route.
- Benches and counters use timber; main structural masses use cream, concrete and slate.
- Curves and chamfered carved forms are preferred at roofs, canopies, vehicles and visible corners.
- Imported city FBX materials and procedural structures pass through the same semantic surface mapping.

## Implementation

- `JinhaeDesign.cs` owns the palette and semantic material mapping.
- `WorldBuilder.Mat` applies it to stations, airports, city models and vehicles.
- `ChangwonWorld.NewMat` applies it to streamed roads and buildings.
- The interface uses the same ink, cream, timber and tactile colours.
# 도시 매스와 성능

서울의 각 구역은 `진해 기준 통합 도시 매스` 한 개 아래에 연속된 외관으로 구성한다. 실제 렌더링은 건물과 60m 도로 구간으로 나눠 거리별로 숨긴다. 화면에서는 하나의 구역으로 보이면서 멀리 있는 블록의 렌더링 비용을 줄인다.

