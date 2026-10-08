"""Build compact Unity meshes and an interactive world atlas from Natural Earth.

Requires numpy, shapely, mapbox-earcut, Pillow. Natural Earth is public domain:
https://www.naturalearthdata.com/about/terms-of-use/
"""
import json
import struct
from pathlib import Path

import mapbox_earcut
import numpy as np
from PIL import Image, ImageDraw
from shapely.geometry import MultiPolygon, Polygon, box, shape
from shapely.ops import unary_union


ROOT = Path(__file__).resolve().parent
DEST = ROOT.parents[1] / "PeninsulaTimeUnity" / "Assets" / "Resources" / "Geo"
DEST.mkdir(parents=True, exist_ok=True)
features = json.loads((ROOT / "ne_10m_admin_0_countries.geojson").read_text())["features"]
by_name = {f["properties"]["ADMIN"]: shape(f["geometry"]) for f in features}


def polygons(geometry):
    if isinstance(geometry, Polygon):
        return [geometry]
    if isinstance(geometry, MultiPolygon):
        return list(geometry.geoms)
    return [item for part in geometry.geoms for item in polygons(part)]


def project(lon, lat):
    return ((lon - 127.7) * 12.0, (lat - 38.0) * 10.0)


region = box(117, 29, 142, 49)
groups = [
    unary_union([by_name["South Korea"], by_name["North Korea"]]),
    by_name["China"], by_name["Russia"], by_name["Japan"],
]
with (DEST / "NortheastAsia.bytes").open("wb") as file:
    file.write(struct.pack("<4sI", b"PTGM", len(groups)))
    for group_id, geometry in enumerate(groups):
        vertices, indices = [], []
        for part in polygons(geometry.intersection(region).simplify(0.012, preserve_topology=True)):
            if part.area < (1e-6 if group_id == 0 else 0.0002):
                continue
            rings = [part.exterior, *part.interiors]
            points, ends = [], []
            for ring in rings:
                points.extend(project(*coord[:2]) for coord in list(ring.coords)[:-1])
                ends.append(len(points))
            if len(points) < 3:
                continue
            array = np.asarray(points, dtype=np.float32)
            triangles = mapbox_earcut.triangulate_float32(array, np.asarray(ends, dtype=np.uint32))
            start = len(vertices)
            vertices.extend(points)
            for a, b, c in triangles.reshape(-1, 3):
                # x/z is a left handed top view in Unity: clockwise faces +Y.
                cross = (points[b][0] - points[a][0]) * (points[c][1] - points[a][1]) - (points[b][1] - points[a][1]) * (points[c][0] - points[a][0])
                indices.extend((start + int(a), start + int(c), start + int(b)) if cross > 0 else (start + int(a), start + int(b), start + int(c)))
        file.write(struct.pack("<BII", group_id, len(vertices), len(indices)))
        for x, z in vertices:
            file.write(struct.pack("<ff", x, z))
        file.write(struct.pack(f"<{len(indices)}I", *indices))
        print("Northeast Asia group", group_id, len(vertices), "vertices", len(indices) // 3, "triangles")


WIDTH, HEIGHT = 2048, 1024
atlas = Image.new("RGB", (WIDTH, HEIGHT), (12, 31, 42))
mask = Image.new("I", (WIDTH, HEIGHT), 0)
draw, mask_draw = ImageDraw.Draw(atlas), ImageDraw.Draw(mask)
featured = ("South Korea", "North Korea", "China", "Japan",
            "United States of America", "Russia", "Germany", "India")
ordered = [next(f for f in features if f["properties"]["ADMIN"] == name) for name in featured]
ordered.extend(f for f in features if f["properties"]["ADMIN"] not in featured)
country_ids = {f["properties"]["ADMIN"]: i + 1 for i, f in enumerate(ordered)}
with (DEST / "WorldCountries.txt").open("w") as countries_file:
    countries_file.write("# index\tname_ko\tiso2\tadmin\n")
    for i, feature in enumerate(ordered):
        p = feature["properties"]
        label = p.get("NAME_KO") or p.get("NAME_EN") or p["ADMIN"]
        countries_file.write(f"{i}\t{label}\t{p.get('ISO_A2', '-99')}\t{p['ADMIN']}\n")


def pixel(lon, lat):
    return (int((lon + 180) / 360 * (WIDTH - 1)), int((90 - lat) / 180 * (HEIGHT - 1)))


for lon in range(-180, 181, 30):
    x, _ = pixel(lon, 0)
    draw.line((x, 0, x, HEIGHT), fill=(23, 49, 59), width=1)
for lat in range(-60, 91, 30):
    _, y = pixel(0, lat)
    draw.line((0, y, WIDTH, y), fill=(23, 49, 59), width=1)

for feature in features:
    name = feature["properties"]["ADMIN"]
    country_id = country_ids[name]
    color = (166, 122, 65) if name in featured else (47, 78, 72)
    for part in polygons(shape(feature["geometry"]).simplify(0.15, preserve_topology=True)):
        if part.area < 0.02:
            continue
        outline = [pixel(*point[:2]) for point in part.exterior.coords]
        draw.polygon(outline, fill=color, outline=(92, 118, 105))
        mask_draw.polygon(outline, fill=country_id)
        for interior in part.interiors:
            hole = [pixel(*point[:2]) for point in interior.coords]
            draw.polygon(hole, fill=(12, 31, 42))
            mask_draw.polygon(hole, fill=0)

atlas.save(DEST / "WorldAtlas.png", optimize=True)
(DEST / "WorldCountryMask.bytes").write_bytes(np.asarray(mask, dtype="<u2").tobytes())
print("World atlas", atlas.size, "saved")
