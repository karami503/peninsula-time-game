#!/usr/bin/env python3
"""Add published Overture footprints where the OSM extract has no buildings.

No credentials or whole-country download required. Existing OSM geometry wins.
Run after build_station_areas.py; reruns replace only this script's supplement.
"""
import argparse
import collections
import datetime
import gzip
import hashlib
import json
import math
import urllib.request

import pyarrow.compute as pc
import pyarrow.dataset as ds
import pyarrow.fs as fs
import pyarrow.parquet as pq
from shapely import wkb

from build_station_areas import HERE, CACHE, OUT, Extract

RELEASE = '2026-09-23.1'
TARGETS = ['r12961370708', 'r368637440', 'r4691566557', 'r4753904292',
           'r5197043949', 'r5208433886', 'scenario-광명시흥', 'scenario-풍양']
PREFIX = 'ovt:'
RAW = CACHE / ('overture-missing8-' + RELEASE + '.parquet')
STAC = CACHE / ('overture-collections-' + RELEASE + '.parquet')
RECEIPT = HERE / 'supplement-098.json'


def fetch(stations, refresh=False):
    if RAW.exists() and not refresh:
        return pq.read_table(RAW)
    CACHE.mkdir(parents=True, exist_ok=True)
    url = f'https://stac.overturemaps.org/{RELEASE}/collections.parquet'
    if not STAC.exists():
        with urllib.request.urlopen(url, timeout=60) as response:
            STAC.write_bytes(response.read())
    boxes = []
    for s in stations:
        dx = 450 / (111320 * math.cos(math.radians(s['lat'])))
        dy = 450 / 111320
        boxes.append((s['lon'] - dx, s['lat'] - dy, s['lon'] + dx, s['lat'] + dy))
    paths = []
    for item in pq.read_table(STAC).to_pylist():
        b = item['bbox']
        if item['collection'] != 'building' or not any(
                b['xmin'] < x2 and b['xmax'] > x1 and b['ymin'] < y2 and b['ymax'] > y1
                for x1, y1, x2, y2 in boxes):
            continue
        # STAC 1.1 uses assets.aws.alternate.s3, older releases used aws-s3.
        asset = item['assets'].get('aws-s3') or item['assets']['aws']['alternate']['s3']
        paths.append(asset['href'].removeprefix('s3://'))
    if not paths:
        raise RuntimeError('No Overture source files intersect these stations')
    expression = None
    for xmin, ymin, xmax, ymax in boxes:
        part = ((pc.field('bbox', 'xmin') < xmax) & (pc.field('bbox', 'xmax') > xmin)
                & (pc.field('bbox', 'ymin') < ymax) & (pc.field('bbox', 'ymax') > ymin))
        expression = part if expression is None else expression | part
    dataset = ds.dataset(paths, filesystem=fs.S3FileSystem(
        anonymous=True, region='us-west-2', connect_timeout=20, request_timeout=60))
    columns = ['id', 'geometry', 'names', 'height', 'min_height', 'num_floors',
               'is_underground', 'subtype', 'sources', 'bbox']
    table = dataset.to_table(filter=expression, columns=columns)
    pq.write_table(table, RAW)
    return table


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--refresh', action='store_true')
    args = parser.parse_args()
    manifest = json.loads((OUT / 'index.json').read_text())
    stations = manifest['stations']
    targets = [s for s in stations if s['id'] in TARGETS]
    if len(targets) != len(TARGETS):
        raise RuntimeError('Station IDs changed; inspect missing-area targets before rebuilding')
    table = fetch(targets, args.refresh)
    old = json.loads(RECEIPT.read_text()) if RECEIPT.exists() else {}
    # Count the original OSM data separately and retain the original date.
    for s in stations:
        s['buildings'] -= s.pop('supplementBuildings', 0)
        s.pop('supplementSource', None)
        s['tiles'] = [k for k in s['tiles'] if k not in old.get('newTileKeys', [])]
    e = Extract([dict(s, tiles=[], roads=0, buildings=0, entrances=0, platforms=0, rails=0)
                 for s in stations], manifest['radius'])
    target_indices = {i for i, s in enumerate(stations) if s['id'] in TARGETS
                      and not (s['buildings'] - s.get('authoredBuildings', 0))}
    datasets = collections.Counter()
    selected = []
    rejected = collections.Counter()
    for row in table.to_pylist():
        shape = wkb.loads(row['geometry'])
        if shape.is_empty or not shape.is_valid:
            rejected['invalidGeometry'] += 1
            continue
        if not target_indices.intersection(e.match(shape)):
            continue
        parts = list(shape.geoms) if shape.geom_type == 'MultiPolygon' else [shape]
        for index, polygon in enumerate(parts):
            if polygon.geom_type != 'Polygon':
                rejected['nonPolygon'] += 1
                continue
            if not target_indices.intersection(e.match(polygon)):
                continue
            tags = {'building': 'yes', 'name': (row['names'] or {}).get('primary', '')}
            if row.get('height') is not None:
                tags['height'] = str(row['height'])
            if row.get('min_height') is not None:
                tags['min_height'] = str(row['min_height'])
            if row.get('num_floors') is not None:
                tags['building:levels'] = str(row['num_floors'])
            if row.get('is_underground'):
                tags['location'] = 'underground'
            ident = PREFIX + row['id'] + ('' if index == 0 else ':' + str(index))
            e.add(ident, 'b', tags, polygon,
                  [list(polygon.exterior.coords)] + [list(r.coords) for r in polygon.interiors])
            selected.append(dict(id=ident, gersId=row['id'], sources=row['sources'],
                                 footprint='published_geospatial_geometry_not_surveyed',
                                 heightSource='published_height' if row.get('height') else
                                 'floors_times_3m_estimate' if row.get('num_floors') else 'default_6m_estimate'))
            datasets.update(s['dataset'] for s in row['sources'])
    source_by_id = {s['id']: s for s in selected}
    changed = []
    new_keys = []
    for path in OUT.glob('tile_*.bytes'):
        tile = json.loads(gzip.decompress(path.read_bytes()))
        filtered = [f for f in tile['features'] if not f['id'].startswith(PREFIX)]
        key = tile['key']
        if len(filtered) != len(tile['features']) or key in e.tiles:
            tile['features'] = filtered
            if key in e.tiles:
                tile['features'].extend(e.tiles.pop(key)['features'])
            changed.append((path, tile))
    for key, tile in e.tiles.items():
        changed.append((OUT / ('tile_' + key + '.bytes'), tile))
        new_keys.append(key)
    for path, tile in changed:
        for feature in tile['features']:
            if feature['id'] in source_by_id:
                feature['source'] = 'Overture Maps ' + RELEASE
                feature['footprintSource'] = 'published_geospatial_geometry_not_surveyed'
                feature['sourceDatasets'] = [s['dataset'] for s in source_by_id[feature['id']]['sources']]
        path.write_bytes(gzip.compress(json.dumps(tile, ensure_ascii=False,
                         separators=(',', ':')).encode(), compresslevel=9, mtime=0))
    per_station = []
    for original, supplement in zip(stations, e.stations):
        if supplement['buildings']:
            original['buildings'] += supplement['buildings']
            original['supplementBuildings'] = supplement['buildings']
            original['supplementSource'] = 'Overture Maps ' + RELEASE
            original['tiles'] = sorted(set(original['tiles'] + supplement['tiles']))
        if original['id'] in TARGETS:
            per_station.append(dict(id=original['id'], name=original['name'],
                plannedOnly=original['plannedOnly'], publishedBuildings=supplement['buildings'],
                status='published_footprints_added' if supplement['buildings'] else
                       'existing_osm_footprints' if original['buildings'] - original.get('authoredBuildings', 0)
                       else 'no_open_footprints_found'))
    manifest['supplement'] = dict(source='https://overturemaps.org', release=RELEASE,
        license='ODbL-1.0', upstream='Qian Shi et al., East Asian Buildings, CC BY 4.0',
        attribution='© OpenStreetMap contributors; Overture Maps Foundation; Qian Shi et al. (2023), doi:10.5281/zenodo.8174931',
        limitations='Published footprint positions, not surveyed interiors. Missing heights remain estimates. Planned stations remain scenarios.')
    (OUT / 'index.json').write_text(json.dumps(manifest, ensure_ascii=False, separators=(',', ':')))
    (HERE / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2))
    receipt = dict(generated=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        sourceRelease=RELEASE, rawSHA256=hashlib.sha256(RAW.read_bytes()).hexdigest(),
        selectedBuildings=len(selected), datasets=dict(datasets), stations=per_station,
        newTileKeys=new_keys, tilesWritten=len(changed), rejected=dict(rejected),
        provenance=selected)
    RECEIPT.write_text(json.dumps(receipt, ensure_ascii=False, indent=2))
    coverage = json.loads((HERE / 'coverage.json').read_text())
    # Original OSM receipt stays intact. Totals after supplement are explicit.
    coverage['supplement098'] = dict(sourceRelease=RELEASE, addedBuildings=len(selected),
        totalStationsWithBuildings=sum(s['buildings'] - s.get('authoredBuildings', 0) > 0 for s in stations),
        stationsWithoutPublishedBuildings=[s['name'] for s in stations
                                          if not (s['buildings'] - s.get('authoredBuildings', 0))],
        runtimeBytes=sum(p.stat().st_size for p in OUT.glob('*') if p.suffix != '.meta'))
    (HERE / 'coverage.json').write_text(json.dumps(coverage, ensure_ascii=False, indent=2))
    print(json.dumps({k: v for k, v in receipt.items() if k != 'provenance'}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
