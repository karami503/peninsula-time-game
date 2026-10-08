"""Extract verified OSM geometry for the four playable Seoul tiles.

Output uses the same local metre coordinates as build_seoul_osm.py and keeps
the OSM object id so each footprint can be checked against its source.
"""
from pathlib import Path
import math
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
INPUT = ROOT / "AssetSources" / "OSM"
OUTPUT = ROOT / "PeninsulaTimeUnity" / "Assets" / "Resources" / "Geo"
DISTRICTS = {
    "Gangnam": ("gangnam", 37.4979, 127.0276),
    "SeoulStation": ("seoulstation", 37.5547, 126.9706),
    "Hongdae": ("hongdae", 37.5572, 126.9236),
    "GimpoAirport": ("gimpo", 37.5588, 126.8025),
}

for district, (source, centre_lat, centre_lon) in DISTRICTS.items():
    tree = ET.parse(INPUT / f"{source}-osm.xml")
    nodes = {n.get("id"): (float(n.get("lat")), float(n.get("lon"))) for n in tree.findall("node")}
    cos_lat = math.cos(math.radians(centre_lat))

    def project(node_id):
        lat, lon = nodes[node_id]
        return (-(lon - centre_lon) * 111320 * cos_lat, -(lat - centre_lat) * 111320)

    rows = ["# OpenStreetMap contributors, ODbL; local Unity x,z metres; P parking, T track (height 0 surface, -5 tunnel), "
            "E subway entrance, R rail platform, S bus stop, N station, A apron stand, G gate, J jet bridge, X terminal"]
    counts = {k: 0 for k in "PTERSNAGJX"}
    for element in tree.getroot():
        if element.tag not in ("node", "way"):
            continue
        tags = {t.get("k"): t.get("v") for t in element.findall("tag")}
        refs = ([element.get("id")] if element.tag == "node" else [n.get("ref") for n in element.findall("nd")])
        points = [project(ref) for ref in refs if ref in nodes]
        if not points or not any(abs(x) < 300 and abs(z) < 300 for x, z in points):
            continue
        kind = None
        if tags.get("amenity") == "parking" and len(points) >= 4 and points[0] == points[-1]:
            kind = "P"
        elif tags.get("railway") in ("rail", "subway") and len(points) >= 2:
            kind = "T"
        elif tags.get("railway") == "subway_entrance" and element.tag == "node":
            kind = "E"
        elif tags.get("railway") == "platform" and element.tag == "way":
            kind = "R"
        elif tags.get("highway") == "bus_stop" and element.tag == "node":
            kind = "S"
        elif tags.get("railway") == "station" and element.tag == "node" and tags.get("name"):
            kind = "N"
        elif tags.get("aeroway") == "parking_position":
            kind = "A"
        elif tags.get("aeroway") == "gate" and element.tag == "node":
            kind = "G"
        elif tags.get("aeroway") == "jet_bridge":
            kind = "J"
        elif tags.get("aeroway") == "terminal" and element.tag == "way":
            kind = "X"
        if kind is None:
            continue
        if kind == "T":
            # Only model tracks that exist. Proposed and construction ways are excluded.
            if tags.get("construction") or tags.get("railway") in ("proposed", "construction"):
                continue
            # Seoul Station's main line is layer -1 (a cutting under the decks) but at grade in the tile.
            try:
                layer = int(float(tags.get("layer", "0").split(";")[0]))
            except ValueError:
                layer = 0
            tunnel = tags.get("tunnel") in ("yes", "building_passage") or layer <= -2 or tags.get("level", "0").startswith("-")
            height = "-5" if tunnel else "0"
            name = tags.get("name", "철길").replace("|", " ")
            fields = [kind, element.get("id"), height, name]
        elif kind == "E":
            fields = [kind, element.get("id"), tags.get("ref", "?"), tags.get("description", "지하철 출입구").replace("|", " ")]
        elif kind == "R":
            if tags.get("tunnel") == "yes" or tags.get("layer", "0").startswith("-") or tags.get("level", "0").startswith("-"):
                continue
            fields = [kind, element.get("id"), tags.get("ref", "").replace("|", " ")]
        elif kind in ("S", "N", "X"):
            fields = [kind, element.get("id"), tags.get("name", "").replace("|", " ")]
        elif kind in ("A", "G", "J"):
            fields = [kind, element.get("id"), (tags.get("ref") or tags.get("name") or "").replace("|", " ")]
        else:
            fields = [kind, element.get("id"), tags.get("name", "주차장").replace("|", " ")]
        fields.append(";".join(f"{x:.2f},{z:.2f}" for x, z in points))
        rows.append("|".join(fields))
        counts[kind] += 1
    OUTPUT.joinpath(f"{district}Features.txt").write_text("\n".join(rows) + "\n", encoding="utf-8")
    print(district, counts)
