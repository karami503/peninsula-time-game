#!/usr/bin/env python3
"""Apply explicitly authored photograph-reference exteriors after public map data.

These are schematic landmarks, not mapped/surveyed building footprints.
The source JSON records the inspected photograph, every chosen dimension,
placement, license, and uncertainty separately from OSM/Overture coverage.
"""
import datetime
import gzip
import hashlib
import json
import math

from shapely.geometry import Polygon
from build_station_areas import HERE, OUT, Extract

PREFIX = 'authored098:'
SOURCE = HERE / 'authored-stations-098.json'


def main():
    source = json.loads(SOURCE.read_text())
    manifest = json.loads((OUT / 'index.json').read_text())
    stations = manifest['stations']
    for station in stations:
        station['buildings'] -= station.pop('authoredBuildings', 0)
        station.pop('authoredSource', None)
    e = Extract([dict(s, tiles=[], roads=0, buildings=0, entrances=0, platforms=0, rails=0)
                 for s in stations], manifest['radius'])
    provenance = {}
    for definition in source['stations']:
        station = next(s for s in stations if s['id'] == definition['id'])
        if station['buildings']:
            continue  # A later public footprint refresh supersedes this authored fallback.
        sx = 111320 * math.cos(math.radians(station['lat']))
        for part in definition['parts']:
            x, y = part['east'], part['north']
            w, d = part['width'] / 2, part['depth'] / 2
            corners = [(x-w, y-d), (x+w, y-d), (x+w, y+d), (x-w, y+d)]
            polygon = Polygon([(station['lon']+px/sx, station['lat']+py/111320)
                               for px, py in corners])
            ident = PREFIX + station['id'] + ':' + part['id']
            tags = dict(building='yes', height=str(part['height']),
                        min_height=str(part['base']),
                        name=station['name']+'역 사진 참고 추정 외관 · '+part['label'])
            e.add(ident, 'b', tags, polygon, [list(polygon.exterior.coords)])
            provenance[ident] = dict(stationId=station['id'], reference=definition['reference'],
                photoAuthor=definition['author'], photoDate=definition['photoDate'],
                photoLicense=definition['photoLicense'], uncertainty=definition['uncertainty'],
                chosenDimensions=part)
    changed = 0
    for path in OUT.glob('tile_*.bytes'):
        tile = json.loads(gzip.decompress(path.read_bytes()))
        before = len(tile['features'])
        tile['features'] = [f for f in tile['features'] if not f['id'].startswith(PREFIX)]
        key = tile['key']
        if before == len(tile['features']) and key not in e.tiles:
            continue
        if key in e.tiles:
            tile['features'].extend(e.tiles.pop(key)['features'])
        write_tile(path, tile, provenance)
        changed += 1
    for key, tile in e.tiles.items():
        write_tile(OUT / ('tile_'+key+'.bytes'), tile, provenance)
        changed += 1
    for station, added in zip(stations, e.stations):
        if added['buildings']:
            station['buildings'] += added['buildings']
            station['authoredBuildings'] = added['buildings']
            station['authoredSource'] = '사진 참고 추정 외관; 측량 윤곽 아님'
            station['tiles'] = sorted(set(station['tiles'] + added['tiles']))
    authored_stations = sorted({record['stationId'] for record in provenance.values()})
    manifest['authoredSupplement'] = dict(classification=source['classification'],
        stations=authored_stations, parts=len(provenance), notice=source['notice'])
    (OUT / 'index.json').write_text(json.dumps(manifest, ensure_ascii=False, separators=(',', ':')))
    (HERE / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2))
    published = lambda s: s['buildings'] - s.get('authoredBuildings', 0)
    coverage = json.loads((HERE / 'coverage.json').read_text())
    coverage['authored098'] = dict(parts=len(provenance), stations=len(authored_stations),
        stationsWithAnyBuildingRepresentation=sum(s['buildings'] > 0 for s in stations),
        stationsWithPublishedBuildings=sum(published(s) > 0 for s in stations),
        stationsWithoutPublishedBuildings=[s['name'] for s in stations if not published(s)],
        runtimeBytes=sum(p.stat().st_size for p in OUT.glob('*') if p.suffix != '.meta'))
    (HERE / 'coverage.json').write_text(json.dumps(coverage, ensure_ascii=False, indent=2))
    receipt = dict(generated=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        sourceSHA256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        parts=len(provenance), tilesWritten=changed, provenance=provenance)
    (HERE / 'authored-098-receipt.json').write_text(json.dumps(receipt, ensure_ascii=False, indent=2))
    print(json.dumps(coverage['authored098'], ensure_ascii=False, indent=2))


def write_tile(path, tile, provenance):
    for feature in tile['features']:
        record = provenance.get(feature['id'])
        if record:
            feature.update(heightSource='authored_photo_estimate',
                widthSource='authored_photo_estimate',
                footprintSource='authored_photo_reference_approximation',
                source='Authored 0.9.8 exterior reference', reference=record['reference'],
                subtype=record['chosenDimensions']['subtype'],
                authored=True, placementSource='schematic_station_forecourt_not_surveyed')
    path.write_bytes(gzip.compress(json.dumps(tile, ensure_ascii=False,
        separators=(',', ':')).encode(), compresslevel=9, mtime=0))


if __name__ == '__main__':
    main()
