"""Fetch the transit network's OpenStreetMap inputs from the Overpass API (with retries across mirrors).

Run: python3 AssetSources/fetch_transit_osm.py [name ...]   (default: all queries; existing files are kept)
Writes AssetSources/OSM/transit/<name>.json.gz, read by build_transit_network.py. Data © OpenStreetMap contributors (ODbL).
"""
import gzip, json, os, sys, time, urllib.request, urllib.parse
HERE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "OSM", "transit")
UA = "PeninsulaTime-transit-build/0.8 (game asset build)"
ENDPOINTS = ["https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter",
             "https://maps.mail.ru/osm/tools/overpass/api/interpreter", "https://overpass.private.coffee/api/interpreter"]
KR = 'area["ISO3166-1"="KR"]->.kr;'
BOXES = {"gangnam": (37.4979, 127.0276), "seoulstation": (37.5547, 126.9706), "hongdae": (37.5572, 126.9236), "gimpo": (37.5588, 126.8025)}
def box(lat, lon, m=330):
    dl = m / 111320.0; dn = m / (111320.0 * 0.7924)
    return f"{lat-dl:.5f},{lon-dn:.5f},{lat+dl:.5f},{lon+dn:.5f}"
QUERIES = {
    "rail_routes": f'[out:json][timeout:240];{KR}rel["route"~"^(subway|light_rail|train|monorail)$"](area.kr)->.r;.r out body;node(r.r)->.n;.n out;',
    "metro_ways": f'[out:json][timeout:280];{KR}(rel["route"~"^(subway|light_rail)$"](area.kr);rel["route"="train"]["service"="commuter"](area.kr);)->.r;way(r.r)["railway"~"^(subway|light_rail|rail)$"];out geom;',
    "main_rail": f'[out:json][timeout:280];{KR}way["railway"="rail"]["usage"="main"][!"service"](area.kr);out geom;',
    "stations": f'[out:json][timeout:200];{KR}(node["railway"~"^(station|halt)$"](area.kr););out;',
}
for name, (lat, lon) in BOXES.items():
    QUERIES["bus_" + name] = f'[out:json][timeout:200];(node["highway"="bus_stop"]({box(lat, lon)});node["public_transport"]({box(lat, lon)});)->.s;way["highway"]({box(lat, lon)})->.w;(rel(bn.s)["route"="bus"];rel(bw.w)["route"="bus"];)->.r;.r out body;node(r.r)->.n;.n out;'
def fetch(name, query):
    path = os.path.join(HERE, name + ".json.gz")
    if os.path.exists(path) and os.path.getsize(path) > 100:
        return True
    os.makedirs(HERE, exist_ok=True)
    for attempt in range(40):
        url = ENDPOINTS[attempt % len(ENDPOINTS)]
        try:
            req = urllib.request.Request(url, data=urllib.parse.urlencode({"data": query}).encode(), headers={"User-Agent": UA})
            body = urllib.request.urlopen(req, timeout=300).read()
            if body[:1] == b"{":
                data = json.loads(body)
                if data.get("remark", "").startswith("runtime error"):
                    raise RuntimeError(data["remark"][:120])
                with gzip.open(path, "wb") as out:
                    out.write(body)
                print(name, "ok", url, len(body), flush=True)
                return True
            raise RuntimeError(body[:200].decode("utf8", "replace").replace("\n", " ")[-120:])
        except Exception as e:
            print(name, "retry", attempt, url, str(e)[:140], flush=True)
            time.sleep(min(60, 8 + attempt * 4))
    return False
ROADS = "도봉로|동소문로|수색로|성산로|강남대로|망우로|왕산로|시흥대로|한강대로|송파대로|통일로|천호대로|양화로|신촌로|공항대로|동작대로|세종대로|종로"
QUERIES["brt_seoul"] = f'[out:json][timeout:200];way["highway"~"^(trunk|primary|secondary)$"]["name"~"^({ROADS})$"](37.42,126.76,37.72,127.19);out geom;'
QUERIES["busways"] = f'[out:json][timeout:200];{KR}way["highway"="busway"](area.kr);out geom;'
if __name__ == "__main__":
    names = sys.argv[1:] or list(QUERIES)
    ok = all([fetch(n, QUERIES[n]) for n in names])
    print("DONE", ok, flush=True)
