"""Rasterise the Korean peninsula (South + North Korea, Natural Earth) into the online battle grid.

Run: python3 AssetSources/Geography/make_korea_grid.py
Writes OnlineServer/korea-grid.txt and PeninsulaTimeUnity/Assets/Resources/Geo/KoreaGrid.txt.
Format: header "W H lon0 lat0 lon1 lat1", then H rows of 0/1 from north to south.
"""
import json
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
W, H = 96, 128
LON0, LON1, LAT0, LAT1 = 124.2, 130.9, 33.0, 43.05
SUPERSAMPLE = 8


def rings(geometry):
    coords = geometry["coordinates"]
    polygons = [coords] if geometry["type"] == "Polygon" else coords
    for polygon in polygons:
        yield polygon[0]


def main():
    features = json.loads((ROOT / "ne_10m_admin_0_countries.geojson").read_text())["features"]
    image = Image.new("L", (W * SUPERSAMPLE, H * SUPERSAMPLE), 0)
    draw = ImageDraw.Draw(image)
    for feature in features:
        if feature["properties"]["ADMIN"] not in ("South Korea", "North Korea"):
            continue
        for ring in rings(feature["geometry"]):
            points = [((lon - LON0) / (LON1 - LON0) * W * SUPERSAMPLE, (LAT1 - lat) / (LAT1 - LAT0) * H * SUPERSAMPLE)
                      for lon, lat in ring]
            draw.polygon(points, fill=255)
    small = image.resize((W, H), Image.BOX)
    rows = ["".join("1" if small.getpixel((x, y)) >= 96 else "0" for x in range(W)) for y in range(H)]
    text = f"{W} {H} {LON0} {LAT0} {LON1} {LAT1}\n" + "\n".join(rows) + "\n"
    for path in (PROJECT / "OnlineServer" / "korea-grid.txt",
                 PROJECT / "PeninsulaTimeUnity" / "Assets" / "Resources" / "Geo" / "KoreaGrid.txt"):
        path.write_text(text)
    land = sum(row.count("1") for row in rows)
    assert 2500 < land < 7000, land
    print("land tiles", land)


main()
