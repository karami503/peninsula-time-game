"""Build Resources/Geo/TransitNetwork.txt: the country's subway, KTX, 무궁화호, bus and BRT network.

Run: python3 AssetSources/build_transit_network.py [overpass-json-dir]

Input: Overpass API JSON (see fetch_transit_osm.py) in AssetSources/OSM/transit/ (*.json or *.json.gz):
  rail_routes   subway / light rail / train route relations with their stop nodes
  metro_ways    track geometry of the urban lines;  main_rail  main-line track geometry
  stations      railway=station|halt nodes;  bus_<district>  bus routes through the four 3D districts
  brt_seoul     Seoul roads with median bus lanes;  busways  highway=busway (BRT) ways
Subway lines, stop orders and bus routes come from OpenStreetMap (ODbL). KTX and 무궁화호 stopping patterns are
written below from the operators' public route maps (major stops, approximate); headways and first/last trains are
game values, so the timetables built from them in the game are a simulation.

Output lines (| separated):
  S|id|name|lon|lat
  L|id|kind|name|short|#colour|peak min|off-peak min|first HHMM|last HHMM|km/h|loop|region
  P|line id|stop ids in order (space separated)
  G|line id|lon,lat;lon,lat;...   track or road geometry, one line per piece
  B|name|lon,lat;...              BRT / median bus lane corridor
"""
import gzip, heapq, json, math, os, re, sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "AssetSources/OSM/transit")
OUT = os.path.join(ROOT, "PeninsulaTimeUnity/Assets/Resources/Geo/TransitNetwork.txt")

def load(name):
    for ext, opener in ((".json", open), (".json.gz", gzip.open)):
        path = os.path.join(SOURCE, name + ext)
        if os.path.exists(path):
            with opener(path, "rt", encoding="utf8") as handle:
                return json.load(handle)["elements"]
    return []

def km(a, b):
    dlat = (b[1] - a[1]) * 111.32
    dlon = (b[0] - a[0]) * 111.32 * math.cos(math.radians((a[1] + b[1]) * .5))
    return math.hypot(dlat, dlon)

def bare(name):
    """Station name for matching: no 역 suffix, no spaces; "김천(구미)" -> "김천구미", "울산(통도사)" -> "울산통도사"."""
    name = re.sub(r"[()\s]", "", name or "")
    return name[:-1] if len(name) > 1 and name.endswith("역") else name

def short_name(name):
    """Without the part in brackets: "울산(통도사)" -> "울산"."""
    return bare(re.sub(r"\(.*?\)", "", name or ""))

def simplify(points, tolerance_km):
    """Ramer-Douglas-Peucker on lon/lat points."""
    if len(points) < 3:
        return points
    keep = [False] * len(points); keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        i, j = stack.pop()
        a, b = points[i], points[j]
        best, index = 0, -1
        cx = math.cos(math.radians(a[1]))
        ax, ay, bx, by = a[0] * cx, a[1], b[0] * cx, b[1]
        length2 = (bx - ax) ** 2 + (by - ay) ** 2
        for k in range(i + 1, j):
            px, py = points[k][0] * cx, points[k][1]
            t = 0 if length2 < 1e-14 else max(0, min(1, ((px - ax) * (bx - ax) + (py - ay) * (by - ay)) / length2))
            d = math.hypot(px - (ax + t * (bx - ax)), py - (ay + t * (by - ay))) * 111.32
            if d > best:
                best, index = d, k
        if best > tolerance_km:
            keep[index] = True
            stack += [(i, index), (index, j)]
    return [p for p, k in zip(points, keep) if k]

class TrackGraph:
    """Track network from way geometries; stations snap to the nearest node, A* finds the track between stops."""
    def __init__(self, ways):
        self.coords, self.index, self.edges = [], {}, defaultdict(list)
        self.grid = defaultdict(list)
        for way in ways:
            previous = None
            for g in way.get("geometry") or []:
                node = self.node((g["lon"], g["lat"]))
                if previous is not None and previous != node:
                    d = km(self.coords[previous], self.coords[node])
                    self.edges[previous].append((node, d)); self.edges[node].append((previous, d))
                previous = node
    def node(self, p):
        key = (round(p[0], 7), round(p[1], 7))
        if key not in self.index:
            self.index[key] = len(self.coords); self.coords.append(p)
            self.grid[(int(p[0] * 50), int(p[1] * 50))].append(self.index[key])
        return self.index[key]
    def nearest(self, p, max_km):
        best, found = max_km, None
        gx, gy = int(p[0] * 50), int(p[1] * 50)
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for n in self.grid.get((gx + dx, gy + dy), ()):
                    d = km(p, self.coords[n])
                    if d < best:
                        best, found = d, n
        return found
    def path(self, a, b, limit_factor=2.2):
        if a is None or b is None:
            return None
        goal = self.coords[b]; straight = km(self.coords[a], goal)
        frontier = [(straight, 0.0, a)]; cost = {a: 0.0}; came = {}
        while frontier:
            _, g, n = heapq.heappop(frontier)
            if n == b:
                out = [n]
                while n in came:
                    n = came[n]; out.append(n)
                return [self.coords[i] for i in reversed(out)]
            if g > cost.get(n, 1e18) or g > straight * limit_factor + 3:
                continue
            for m, d in self.edges[n]:
                ng = g + d
                if ng < cost.get(m, 1e18):
                    cost[m] = ng; came[m] = n
                    heapq.heappush(frontier, (ng + km(self.coords[m], goal), ng, m))
        return None

def route_shape(stops, graph, snap_km, tolerance):
    """Track geometry through the stops in order; straight pieces where the track is missing."""
    shape = []
    snapped = [graph.nearest(p, snap_km) for p in stops]
    for i in range(1, len(stops)):
        piece = graph.path(snapped[i - 1], snapped[i])
        if not piece:
            piece = [stops[i - 1], stops[i]]
        shape += piece if not shape else piece[1:]
    return simplify(shape, tolerance)

# ---------------------------------------------------------------- stations
station_nodes = [e for e in load("stations") if e["type"] == "node" and "name" in e.get("tags", {})]
rail_elements = load("rail_routes")
stop_nodes = {e["id"]: e for e in rail_elements if e["type"] == "node"}
relations = [e for e in rail_elements if e["type"] == "relation"]
station_grid = defaultdict(list)
for s in station_nodes:
    station_grid[(int(s["lon"] * 50), int(s["lat"] * 50))].append(s)

def ko(tags):
    return tags.get("name:ko") or tags.get("name")

def station_name_near(p, max_km=.45):
    best, name = max_km, None
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for s in station_grid.get((int(p[0] * 50) + dx, int(p[1] * 50) + dy), ()):
                d = km(p, (s["lon"], s["lat"]))
                if d < best:
                    best, name = d, ko(s["tags"])
    return name

stations = {}          # id -> [name, lon sum, lat sum, count]
by_name = defaultdict(list)
by_short = defaultdict(list)  # "울산" for "울산(통도사)", used only when no station has the plain name

def centre(sid):
    s = stations[sid]
    return (s[1] / s[3], s[2] / s[3])

def station_for(name, p, key=None, merge_km=.9):
    """Station id for a stop: stops with the same name within merge_km share one station (transfers)."""
    name = name.strip()
    for sid in by_name[bare(name)] + by_short[short_name(name)] + by_name[short_name(name)]:
        if km(p, centre(sid)) < merge_km:
            s = stations[sid]; s[1] += p[0]; s[2] += p[1]; s[3] += 1
            return sid
    sid = "r" + str(key if key is not None else "x%d" % len(stations))
    stations[sid] = [name, p[0], p[1], 1]
    by_name[bare(name)].append(sid)
    if short_name(name) != bare(name):
        by_short[short_name(name)].append(sid)
    return sid

for s in station_nodes:  # rail stations first so KTX/무궁화 names resolve to them
    station_for(ko(s["tags"]), (s["lon"], s["lat"]), key=s["id"], merge_km=.6)
# OSM way 461556215 is a station building/platform, absent from the node-only Overpass query.
# Coordinates: OSM via Mapcarta, 36.41032 N, 128.16377 E; 경북선 between 청리 and 함창.
if not by_name.get("상주"):
    station_for("상주", (128.16377, 36.41032), key=461556215)

def station_by_name(name):
    for alternative in name.split("/"):
        ids = by_name.get(bare(alternative)) or by_short.get(bare(alternative))
        if ids:
            return ids[0]
    return None

# ---------------------------------------------------------------- urban lines
METRO_NAMES = {"1": "1호선", "2": "2호선", "3": "3호선", "4": "4호선", "5": "5호선", "6": "6호선", "7": "7호선", "8": "8호선",
               "9": "9호선", "신분당": "신분당선", "공항철도": "공항철도", "경의·중앙": "경의·중앙선", "경춘": "경춘선",
               "수인·분당": "수인·분당선", "경강": "경강선", "서해": "서해선", "김포 골드라인": "김포골드라인", "W": "우이신설선",
               "Silim": "신림선", "U": "의정부경전철", "E": "용인에버라인", "인천1": "인천 1호선", "I2": "인천 2호선", "GTX-A": "GTX-A",
               "BGL": "부산김해경전철", "동해": "동해선", "대경": "대경선"}
HEADWAYS = {"1": (5, 9), "2": (2.5, 5), "3": (3, 6), "4": (3, 6), "5": (3, 6), "6": (4, 7), "7": (3, 6), "8": (4, 7), "9": (3, 6),
            "신분당": (4, 6), "공항철도": (6, 12), "경의·중앙": (8, 15), "경춘": (12, 20), "수인·분당": (5, 10), "경강": (10, 15),
            "서해": (8, 15), "김포 골드라인": (3, 6), "W": (4, 6), "Silim": (4, 6), "U": (4, 6), "E": (5, 8), "인천1": (4, 7),
            "I2": (4, 7), "GTX-A": (10, 15), "BGL": (5, 8), "동해": (12, 20), "대경": (20, 30)}
OTHER_CITIES = {"1": (4, 7), "2": (5, 7), "3": (5, 8), "4": (6, 9)}

def region_of(network):
    for key in ("부산", "대구", "대전", "광주"):
        if key in network:
            return key
    return "수도권"

def stop_list(relation):
    out = []
    for m in relation["members"]:
        if m["type"] != "node" or not m["role"].startswith("stop") or m["ref"] not in stop_nodes:
            continue
        n = stop_nodes[m["ref"]]
        p = (n["lon"], n["lat"])
        name = ko(n.get("tags", {})) or station_name_near(p)
        if name:
            out.append((name, p))
    return out

groups = defaultdict(list)
for r in relations:
    t = r["tags"]
    network, ref = t.get("network", ""), t.get("ref", "")
    urban = t.get("route") in ("subway", "light_rail", "monorail") or t.get("service") == "commuter" or ref == "GTX-A"
    if not urban or not ref or "자기부상" in network or (ref not in METRO_NAMES and not ref.isdigit()):
        continue
    if network == "" and ref == "2":  # 광주 2호선: not open yet
        continue
    region = region_of(network + " " + t.get("name", ""))
    stops = stop_list(r)
    if len(stops) >= 2:
        groups[(region, ref)].append((r, stops))

metro_graph = TrackGraph([w for w in load("metro_ways") if w["type"] == "way"])
lines = []
for (region, ref), variants in sorted(groups.items()):
    variants.sort(key=lambda v: -len(v[1]))
    covered = set()
    base = METRO_NAMES.get(ref, ref + "호선")
    short = base if region == "수도권" or not ref.isdigit() else region + " " + base
    peak, off = HEADWAYS.get(ref, (5, 8)) if region == "수도권" or not ref.isdigit() else OTHER_CITIES.get(ref, (6, 9))
    colour = variants[0][0]["tags"].get("colour", "#888888")
    first_variant = True
    for r, stops in variants:
        ids = []
        for name, p in stops:
            sid = station_for(name, p)
            if not ids or ids[-1] != sid:
                ids.append(sid)
        if covered and len(set(ids) - covered) < 2:  # same stops as a longer variant (other direction, express)
            continue
        covered.update(ids)
        loop = len(ids) > 3 and ids[0] == ids[-1]
        tag_name = r["tags"].get("name", "")
        variant = ""
        if "지선" in tag_name:
            variant = " " + next(w for w in tag_name.split() if "지선" in w).rstrip(":")
        elif not first_variant:
            variant = " (%s–%s)" % (bare(stops[0][0]), bare(stops[-1][0]))
        first_variant = False
        fast = ref in ("GTX-A", "공항철도", "신분당")
        lines.append(dict(id="o%d" % r["id"], kind="metro", name=short + variant, short=short, colour=colour, peak=peak, off=off,
                          first="0530", last="2400", speed=55 if fast else 34, loop=loop, region=region, stops=ids,
                          shape=route_shape([p for _, p in stops], metro_graph, .6, .012)))

# ---------------------------------------------------------------- KTX and 무궁화호 (major stops, approximate)
KTX = [
    ("k-gyeongbu", "KTX 경부선", (20, 30), 150, "서울 광명 천안아산 오송 대전 김천구미 동대구 경주/신경주 울산 부산"),
    ("k-honam", "KTX 호남선", (45, 60), 150, "용산 광명 천안아산 오송 공주 익산 정읍 광주송정 나주 목포"),
    ("k-jeolla", "KTX 전라선", (90, 120), 130, "용산 광명 천안아산 오송 익산 전주 남원 곡성 구례구 순천 여천 여수엑스포"),
    ("k-gyeongjeon", "KTX 경전선", (90, 120), 130, "서울 광명 천안아산 오송 대전 동대구 밀양 진영 창원중앙 창원 마산 진주"),
    ("k-donghae", "KTX 동해선", (90, 120), 140, "서울 광명 천안아산 오송 대전 동대구 포항"),
    ("k-gangneung", "KTX 강릉선", (60, 90), 120, "서울 청량리 상봉 양평 만종 횡성 둔내 평창 진부 강릉"),
    ("k-jungang", "KTX-이음 중앙선", (90, 120), 110, "청량리 양평 서원주 제천 단양 풍기 영주 안동 의성 영천 경주/신경주 태화강 부전"),
    ("k-jungbu", "KTX-이음 중부내륙선", (120, 180), 100, "판교 부발 가남 감곡장호원 앙성온천 충주 살미 수안보온천 연풍 문경"),
]
MUGUNGHWA = [
    ("g-gyeongbu", "무궁화호 경부선", (60, 90), "서울 영등포 안양 수원 오산 서정리 평택 성환 천안 전의 조치원 부강 신탄진 대전 옥천 이원 지탄 심천 영동 황간 추풍령 김천 구미 사곡 약목 왜관 서대구 대구 동대구 경산 남성현 청도 상동 밀양 삼랑진 원동 물금 화명 구포 사상 부산"),
    ("g-honam", "무궁화호 호남선", (120, 180), "용산 영등포 수원 평택 천안 조치원 서대전 계룡 연산 논산 강경 함열 익산 김제 신태인 정읍 백양사 장성 광주송정 나주 함평 무안 몽탄 일로 임성리 목포"),
    ("g-jeolla", "무궁화호 전라선", (120, 180), "용산 영등포 수원 평택 천안 조치원 서대전 계룡 논산 익산 삼례 전주 임실 오수 남원 곡성 구례구 순천 여천 여수엑스포"),
    ("g-janghang", "무궁화호 장항선", (120, 180), "용산 영등포 수원 평택 천안 아산 신창 도고온천 신례원 예산 삽교 홍성 광천 청소 대천 웅천 판교 서천 장항 군산 대야 익산"),
    ("g-jungang", "무궁화호 중앙선", (120, 180), "청량리 덕소 양평 용문 원주 제천 단양 풍기 영주 안동"),
    ("g-taebaek", "무궁화호 태백선", (150, 210), "청량리 양평 원주 제천 쌍용 영월 예미 민둥산 사북 고한 태백 철암 도계 동해 묵호 정동진 강릉"),
    ("g-yeongdong", "무궁화호 영동선", (180, 240), "동대구 하양 영천 의성 안동 영주 봉화 춘양 분천 석포 승부 철암 도계 동해 묵호 정동진 강릉"),
    ("g-gyeongjeon", "무궁화호 경전선", (120, 180), "부전 사상 구포 화명 물금 원동 삼랑진 한림정 진영 창원중앙 창원 마산 중리 함안 군북 반성 진주"),
    ("g-gyeongbuk", "무궁화호 경북선", (180, 240), "김천 옥산 청리 상주 함창 점촌 용궁 예천 영주"),
    ("g-chungbuk", "무궁화호 충북선", (150, 210), "대전 신탄진 조치원 오송 청주 오근장 청주공항 증평 음성 주덕 충주 삼탄 봉양 제천"),
    ("g-donghae", "무궁화호 동해선", (150, 210), "부전 센텀 신해운대 기장 좌천 남창 태화강 북울산 경주/신경주 영천 하양 동대구"),
    ("g-gyooe", "무궁화호 교외선", (60, 90), "대곡 원릉 일영 장흥 송추 의정부"),
]
rail_graph = TrackGraph([w for w in load("main_rail") if w["type"] == "way"] + [w for w in load("metro_ways") if w["type"] == "way"])
missing = []
def pattern(stop_names):
    ids = []
    for name in stop_names.split():
        sid = station_by_name(name)
        if sid is None:
            missing.append(name)
        elif not ids or ids[-1] != sid:
            ids.append(sid)
    return ids
for lid, name, (peak, off), speed, stop_names in KTX:
    ids = pattern(stop_names)
    lines.append(dict(id=lid, kind="ktx", name=name, short="KTX", colour="#1E5BA8", peak=peak, off=off, first="0515", last="2240",
                      speed=speed, loop=False, region="전국", stops=ids, shape=route_shape([centre(i) for i in ids], rail_graph, 1.5, .08)))
for lid, name, (peak, off), stop_names in MUGUNGHWA:
    ids = pattern(stop_names)
    lines.append(dict(id=lid, kind="mugunghwa", name=name, short="무궁화호", colour="#D1495B", peak=peak, off=off, first="0530", last="2300",
                      speed=70, loop=False, region="전국", stops=ids, shape=route_shape([centre(i) for i in ids], rail_graph, 1.5, .08)))

# ---------------------------------------------------------------- bus routes through the 3D districts
BUS_TYPES = [("광역", "#E60012", (12, 20)), ("간선", "#3D5BAB", (6, 9)), ("지선", "#5BB025", (7, 11)), ("순환", "#F2B70A", (8, 12)),
             ("마을", "#83C341", (10, 15)), ("공항", "#8A8A8A", (20, 30)), ("경기", "#0095DA", (12, 18)), ("인천", "#0095DA", (12, 18))]
bus_routes = {}
for district in ("gangnam", "seoulstation", "hongdae", "gimpo"):
    elements = load("bus_" + district)
    nodes = {e["id"]: e for e in elements if e["type"] == "node"}
    for r in (e for e in elements if e["type"] == "relation"):
        t = r["tags"]; ref = t.get("ref") or t.get("name", "")
        if not ref:
            continue
        stops = []
        for m in r["members"]:
            n = nodes.get(m["ref"]) if m["type"] == "node" else None
            if n is None or not (m["role"].startswith("platform") or m["role"].startswith("stop")):
                continue
            name = n.get("tags", {}).get("name")
            if name:
                stops.append((m["ref"], name, (n["lon"], n["lat"])))
        if len(stops) < 2:
            continue
        label = t.get("network", "") + " " + t.get("name", "") + " " + t.get("bus", "")
        colour, headway = next(((c, h) for key, c, h in BUS_TYPES if key in label), ("#3D5BAB", (8, 12)))
        if ref not in bus_routes or len(bus_routes[ref]["stops"]) < len(stops):
            bus_routes[ref] = dict(colour=t.get("colour", colour), headway=headway, stops=stops, rid=r["id"], ends=(t.get("from"), t.get("to")))
for ref, b in sorted(bus_routes.items()):
    ids = []
    for nid, name, p in b["stops"]:
        sid = "b%d" % nid
        stations.setdefault(sid, [name, p[0], p[1], 1])
        if not ids or ids[-1] != sid:
            ids.append(sid)
    # The route's own termini when tagged: OSM often maps only part of a Seoul route's stops.
    ends = "–".join(b["ends"]) if all(b["ends"]) else b["stops"][0][1] + "–" + b["stops"][-1][1]
    lines.append(dict(id="o%d" % b["rid"], kind="bus", name="%s번 (%s)" % (ref, ends), short=ref, colour=b["colour"], peak=b["headway"][0],
                      off=b["headway"][1], first="0430", last="2330", speed=17, loop=len(ids) > 3 and ids[0] == ids[-1],
                      region="서울 버스", stops=ids, shape=[]))

# ---------------------------------------------------------------- BRT corridors
def chain(polylines):
    """Join ways that meet end to end into longer polylines (OSM splits roads at every junction)."""
    key = lambda p: (round(p[0], 5), round(p[1], 5))
    pending, merged = [list(p) for p in polylines if len(p) >= 2], []
    while pending:
        cur, grown = pending.pop(), True
        while grown:
            grown = False
            for i, o in enumerate(pending):
                if key(o[0]) == key(cur[-1]):
                    cur = cur + o[1:]
                elif key(o[-1]) == key(cur[-1]):
                    cur = cur + o[::-1][1:]
                elif key(o[-1]) == key(cur[0]):
                    cur = o + cur[1:]
                elif key(o[0]) == key(cur[0]):
                    cur = o[::-1] + cur[1:]
                else:
                    continue
                pending.pop(i)
                grown = True
                break
        merged.append(cur)
    return merged


by_label = {}
for w in load("brt_seoul") + load("busways"):
    if w["type"] != "way" or not w.get("geometry"):
        continue
    tags = w.get("tags", {})
    label = tags.get("name", "") + (" BRT" if tags.get("highway") == "busway" else " 중앙버스전용차로")
    by_label.setdefault(label.strip(), []).append([(g["lon"], g["lat"]) for g in w["geometry"]])
def chain_km(points):
    return sum(math.hypot((b[0] - a[0]) * 88.2, (b[1] - a[1]) * 111.3) for a, b in zip(points, points[1:]))


# Median bus lanes are whole roads; busways (BRT) also include short bus-only links and ramps, kept from 500 m up.
corridors = [(label if label != "BRT" else "BRT 전용도로", simplify(points, .01)) for label, ways in sorted(by_label.items())
             for points in chain(ways) if not label.endswith("BRT") or chain_km(points) >= .5]

# ---------------------------------------------------------------- write
used = set()
for line in lines:
    used.update(line["stops"])
with open(OUT, "w", encoding="utf8") as out:
    out.write("# 반도의 시간 교통망 · 지도 데이터 © OpenStreetMap 기여자 (ODbL) · KTX·무궁화호 정차 패턴과 배차는 게임용 근사값\n")
    for sid in sorted(used):
        lon, lat = centre(sid)
        out.write("S|%s|%s|%.6f|%.6f\n" % (sid, stations[sid][0], lon, lat))
    for line in lines:
        if len(line["stops"]) < 2:
            continue
        out.write("L|%s|%s|%s|%s|%s|%g|%g|%s|%s|%g|%d|%s\n" % (line["id"], line["kind"], line["name"], line["short"], line["colour"],
                  line["peak"], line["off"], line["first"], line["last"], line["speed"], 1 if line["loop"] else 0, line["region"]))
        out.write("P|%s|%s\n" % (line["id"], " ".join(line["stops"])))
        if line["shape"]:
            out.write("G|%s|%s\n" % (line["id"], ";".join("%.5f,%.5f" % p for p in line["shape"])))
    for label, points in corridors:
        out.write("B|%s|%s\n" % (label, ";".join("%.5f,%.5f" % p for p in points)))
print("stations", len(used), "lines", len(lines), "metro", sum(l["kind"] == "metro" for l in lines),
      "bus", sum(l["kind"] == "bus" for l in lines), "brt corridors", len(corridors))
if missing:
    print("rail stops not found:", " ".join(sorted(set(missing))))
