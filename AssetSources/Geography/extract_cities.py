"""Extract named Korean city/town centres from Geofabrik OSM PBF files.

Usage: python extract_cities.py south-korea-latest.osm.pbf north-korea-latest.osm.pbf
Requires pyosmium and shapely. The resulting city data is ODbL; see
https://www.openstreetmap.org/copyright.
"""
import csv
import json
import sys
from pathlib import Path

import osmium
from shapely.geometry import Point, shape


ROOT = Path(__file__).resolve().parent
countries = json.loads((ROOT / "ne_10m_admin_0_countries.geojson").read_text())
korea = {
    item["properties"]["ADMIN"]: shape(item["geometry"])
    for item in countries["features"]
    if item["properties"]["ADMIN"] in {"South Korea", "North Korea"}
}


class Places(osmium.SimpleHandler):
    def __init__(self, country):
        super().__init__()
        self.country = country
        self.items = []

    def node(self, node):
        if not node.location.valid():
            return
        tags = node.tags
        kind = tags.get("place")
        if kind not in ("city", "town"):
            return
        name = tags.get("name:ko") or tags.get("name")
        if not name:
            return
        lon, lat = node.location.lon, node.location.lat
        if not korea[self.country].buffer(0.03).covers(Point(lon, lat)):
            return
        self.items.append((int(node.id), kind, name.strip(), lat, lon, tags.get("population", "")))


out = []
for filename, country in zip(sys.argv[1:], ("South Korea", "North Korea")):
    parser = Places(country)
    parser.apply_file(filename, locations=False)
    print(country, len(parser.items))
    out.extend((country, *item) for item in parser.items)

out.sort(key=lambda row: (row[0] != "South Korea", row[3], row[2]))
with (ROOT / "korean_city_centres.csv").open("w", newline="") as handle:
    writer = csv.writer(handle)
    writer.writerow(("country", "osm_node_id", "place", "name_ko", "latitude", "longitude", "population_tag"))
    writer.writerows(out)
print("Wrote", len(out), "places")
