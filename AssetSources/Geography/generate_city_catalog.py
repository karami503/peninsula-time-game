"""Build in-game Korean place search data from OSM city/town centres.

The first fifteen IDs remain stable for older save games. Other IDs use OSM
node IDs. OSM derived output is offered under ODbL with attribution:
https://www.openstreetmap.org/copyright.
"""
import csv
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DEST = ROOT.parents[1] / "PeninsulaTimeUnity" / "Assets" / "Resources" / "Geo" / "KoreanPlaces.txt"
rows = list(csv.DictReader((ROOT / "korean_city_centres.csv").open()))

original = [
    ("seoul", "서울", 37.5665, 126.9780), ("incheon", "인천", 37.4563, 126.7052),
    ("suwon", "수원", 37.2636, 127.0286), ("chuncheon", "춘천", 37.8813, 127.7298),
    ("gangneung", "강릉", 37.7519, 128.8761), ("daejeon", "대전", 36.3504, 127.3845),
    ("cheongju", "청주", 36.6424, 127.4890), ("jeonju", "전주", 35.8242, 127.1480),
    ("gwangju", "광주", 35.1595, 126.8526), ("daegu", "대구", 35.8714, 128.6014),
    ("pohang", "포항", 36.0190, 129.3435), ("changwon", "창원", 35.2281, 128.6811),
    ("busan", "부산", 35.1796, 129.0756), ("ulsan", "울산", 35.5384, 129.3114),
    ("jeju", "제주", 33.4996, 126.5312),
    # Separate playable districts for the three cities consolidated in 2010.
    ("masan", "마산", 35.1969, 128.5679),
    ("jinhae", "진해", 35.1496, 128.6597),
]
# Former cities later merged into another city, playable separately as a game rule.
# Coordinates are approximate old city centres.
former = [
    ("songjeong", "송정", 35.1378, 126.7935),    # 광주 광산구, 1988
    ("migeum", "미금", 37.6085, 127.1620),       # 남양주, 1995
    ("songtan", "송탄", 37.0610, 127.0580),      # 평택, 1995
    ("onyang", "온양", 36.7805, 127.0030),       # 아산, 1995
    ("daecheon", "대천", 36.3405, 126.6040),     # 보령, 1995
    ("jeomchon", "점촌", 36.5880, 128.1870),     # 문경, 1995
    ("iri", "이리", 35.9435, 126.9460),          # 익산, 1995
    ("donggwangyang", "동광양", 34.9400, 127.6990),  # 광양, 1995
    ("samcheonpo", "삼천포", 34.9355, 128.0825),  # 사천, 1995
    ("chungmu", "충무", 34.8460, 128.4230),      # 통영, 1995
    ("jangseungpo", "장승포", 34.8650, 128.7270),  # 거제, 1995
    ("yeocheon", "여천", 34.7600, 127.6620),     # 여수, 1998
]
lines = []
for identifier, name, lat, lon in original:
    lines.append((identifier, name, "남한", lon, lat, "city", 1))
for identifier, name, lat, lon in former:
    lines.append((identifier, name, "남한", lon, lat, "city", 0))

for row in rows:
    lat, lon = float(row["latitude"]), float(row["longitude"])
    name = row["name_ko"].replace("\t", " ").replace("\n", " ")
    if not any("가" <= char <= "힣" for char in name):
        continue
    # Suppress a second OSM pin at a preserved save-game city's centre.
    if any(abs(lat - base_lat) < .12 and abs(lon - base_lon) < .12 and
           (name == base_name or name.startswith(base_name + "시") or name.startswith(base_name + "광역"))
           for _, base_name, base_lat, base_lon in original + former):
        continue
    lines.append(("osm" + row["osm_node_id"], name,
                  "북한" if row["country"] == "North Korea" else "남한",
                  lon, lat, row["place"], 0))

with DEST.open("w") as handle:
    handle.write("# id\tname\tregion\tlongitude\tlatitude\tplace\tfeatured\n")
    for identifier, name, region, lon, lat, kind, featured in lines:
        handle.write(f"{identifier}\t{name}\t{region}\t{lon:.7f}\t{lat:.7f}\t{kind}\t{featured}\n")
print("Catalog entries", len(lines), "cities", sum(x[5] == "city" for x in lines))
