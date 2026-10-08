"""Build the four modern Seoul districts from OpenStreetMap extracts.

Run: Blender -b --factory-startup -P AssetSources/build_seoul_osm.py
Writes Models/<District>OSM.fbx plus, in Unity metres (x, z = -east, -north):
  Geo/<District>Roads.txt  car and bus lanes: "[B]<width>[>] x,z[,y] ..."; B = bus only,
                           > = one way in the listed direction; y only on bridges.
  Geo/<District>Walks.txt  pedestrian network: sidewalks, zebra crossings, footways.
Car roads, bus lanes, sidewalks, footways, bus/rail platforms, bridges and rail beds are
separate meshes, so platforms, busways and footways never become car lanes and tunnels stay
below ground.
"""
import bpy
import math
import os
import xml.etree.ElementTree as ET
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "PeninsulaTimeUnity/Assets/Resources/Models")
SOURCES = os.path.join(ROOT, "AssetSources/OSM")
GEO_OUT = os.path.join(ROOT, "PeninsulaTimeUnity/Assets/Resources/Geo")
os.makedirs(OUT, exist_ok=True)

DISTRICTS = [
    ("Gangnam", "gangnam", 37.4979, 127.0276),
    ("SeoulStation", "seoulstation", 37.5547, 126.9706),
    ("Hongdae", "hongdae", 37.5572, 126.9236),
    ("GimpoAirport", "gimpo", 37.5588, 126.8025),
]

CAR_TYPES = {"motorway", "trunk", "primary", "secondary", "tertiary", "unclassified", "residential",
             "living_street", "service", "road"}
WALK_TYPES = {"footway", "path", "pedestrian", "steps", "cycleway", "track", "bridleway"}
NO_CARS = {"no", "private", "delivery", "destination_only"}
LAYER_HEIGHT, RAMP, PIER_SPACING = 6.0, 35.0, 24.0
FLOOR_HEIGHT, BAY_WIDTH, ROAD_REPEAT = 3.2, 3.0, 10.0
FACADE_BAYS = 4  # facade textures hold 4 bays x 4 floors
CLIP = 310
# Seoul's median bus lanes (중앙버스전용차로) on the dual carriageways of these roads, per district. OSM maps the
# carriageways but not the bus lanes, so each carriageway gets a red bus lane along its inner (median) edge.
MEDIAN_BUS_ROADS = {"gangnam": {"강남대로"}, "seoulstation": {"한강대로", "통일로", "세종대로"},
                    "hongdae": {"양화로", "신촌로"}, "gimpo": set()}
BUS_LANE_WIDTH, MEDIAN_ROAD_WIDTH = 3.5, 16
ROAD_Z, SIDEWALK_Z, FOOTWAY_Z = 0.055, 0.14, 0.09


def material(name, color):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    return mat


MATS = {
    "Ground": material("Terrain olive", (0.32, 0.43, 0.34)),
    "Road": material("Asphalt graphite", (0.16, 0.20, 0.23)),
    "Busway": material("Bus lane red", (0.52, 0.20, 0.17)),
    "Footway": material("Walking stone", (0.55, 0.56, 0.52)),
    "Sidewalk": material("Sidewalk paving", (0.60, 0.58, 0.54)),
    "Curb": material("Curb granite", (0.70, 0.70, 0.67)),
    "Bridge": material("Bridge concrete", (0.63, 0.63, 0.60)),
    "Railing": material("Railing steel", (0.40, 0.44, 0.47)),
    "Platform": material("Platform concrete", (0.70, 0.69, 0.65)),
    "PlatformEdge": material("Platform edge yellow", (0.93, 0.76, 0.15)),
    "Canopy": material("Canopy roof", (0.82, 0.84, 0.85)),
    "Ballast": material("Track ballast", (0.36, 0.34, 0.32)),
    "Sleeper": material("Track sleeper", (0.48, 0.47, 0.45)),
    "Rail": material("Rail steel", (0.66, 0.68, 0.70)),
    "Apron": material("Apron concrete", (0.66, 0.66, 0.63)),
    "Taxiway": material("Taxiway concrete", (0.52, 0.52, 0.50)),
    "TaxiLine": material("Taxiway line yellow", (0.95, 0.78, 0.12)),
    "Building": material("Building stone", (0.60, 0.63, 0.61)),
    "BuildingGrey": material("Facade grey", (0.78, 0.77, 0.73)),
    "BuildingWhite": material("Facade white", (0.89, 0.89, 0.87)),
    "BuildingBeige": material("Facade beige", (0.83, 0.75, 0.61)),
    "BuildingBrick": material("Facade brick", (0.59, 0.28, 0.20)),
    "BuildingGlass": material("Facade glass", (0.27, 0.43, 0.50)),
    "Junction": material("Junction asphalt", (0.16, 0.20, 0.23)),
    "Crosswalk": material("Crosswalk paint", (0.90, 0.90, 0.86)),
    "Roof": material("Roof slate", (0.26, 0.31, 0.34)),
}


# Geometry authored from the district Roadview survey, not extracted imagery.
for name, color in {
    "FacadeFrame": (.30,.33,.35), "FacadeTrim": (.68,.69,.66),
    "FacadeGlazing": (.18,.31,.35), "FacadeGlazingLight": (.34,.47,.48),
    "FacadeShop": (.13,.20,.22), "FacadeSign": (.18,.23,.25),
    "FacadeAwningRed": (.48,.15,.12), "FacadeAwningCream": (.75,.68,.53),
    "FacadeSoffit": (.72,.74,.72), "FacadeSteel": (.54,.59,.60),
}.items(): MATS[name] = material(name, color)


def mesh_object(name, verts, faces, uvs, parent):
    if not faces:
        return
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    layer = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        layer.data[loop.index].uv = uvs[loop.vertex_index]
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    obj.data.materials.append(MATS[name])


def facade_style(way_id, height):
    """Deterministic facade mix: tall towers are glass, low buildings brick or beige."""
    pick = (way_id * 2654435761) % 100
    if height >= 45:
        return "BuildingGlass" if pick < 70 else "BuildingWhite"
    if height <= 10:
        return "BuildingBrick" if pick < 45 else "BuildingBeige"
    return ("BuildingGrey", "BuildingWhite", "BuildingBeige", "BuildingGlass", "BuildingBrick")[pick % 5]


def clip_polygon(points, extent=CLIP):
    """Clip OSM footprints and pavement to the playable square before meshing."""
    out = points
    for axis, bound, keep_less in ((0, -extent, False), (0, extent, True), (1, -extent, False), (1, extent, True)):
        if not out:
            return []
        source, out = out, []
        for previous, current in zip(source[-1:] + source[:-1], source):
            inside_p = previous[axis] <= bound if keep_less else previous[axis] >= bound
            inside_c = current[axis] <= bound if keep_less else current[axis] >= bound
            if inside_p != inside_c:
                t = (bound - previous[axis]) / (current[axis] - previous[axis])
                out.append(tuple(previous[i] + t * (current[i] - previous[i]) for i in range(len(current))))
            if inside_c:
                out.append(current)
    return out


def layer_of(tags):
    try:
        return int(float(tags.get("layer", "0").split(";")[0]))
    except ValueError:
        return 0


def underground(tags):
    return (tags.get("tunnel") in ("yes", "building_passage", "culvert") or layer_of(tags) < 0
            or tags.get("location") == "underground" or tags.get("indoor") == "yes")


def elevated(tags):
    return tags.get("bridge") not in (None, "no") or layer_of(tags) >= 1


def join_rings(member_ways):
    """Join multipolygon member ways (lists of node ids) into closed rings."""
    rings, open_ways = [], [list(w) for w in member_ways if len(w) >= 2]
    while open_ways:
        ring = open_ways.pop()
        changed = True
        while ring[0] != ring[-1] and changed:
            changed = False
            for i, way in enumerate(open_ways):
                if way[0] == ring[-1]:
                    ring += way[1:]
                elif way[-1] == ring[-1]:
                    ring += way[-2::-1]
                elif way[-1] == ring[0]:
                    ring = way[:-1] + ring
                elif way[0] == ring[0]:
                    ring = way[:0:-1] + ring
                else:
                    continue
                open_ways.pop(i)
                changed = True
                break
        if ring[0] == ring[-1] and len(ring) >= 4:
            rings.append(ring)
    return rings


def seg_distance(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    length2 = dx * dx + dy * dy
    t = 0 if length2 < 1e-9 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / length2))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


class SegmentGrid:
    """Spatial hash of carriageway segments: (ax, ay, bx, by, half width, height, owner)."""

    def __init__(self, cell=20.0):
        self.cell, self.cells = cell, {}

    def add(self, seg):
        ax, ay, bx, by = seg[:4]
        c = self.cell
        for gx in range(int(math.floor(min(ax, bx) / c)) - 1, int(math.floor(max(ax, bx) / c)) + 2):
            for gy in range(int(math.floor(min(ay, by) / c)) - 1, int(math.floor(max(ay, by) / c)) + 2):
                self.cells.setdefault((gx, gy), []).append(seg)

    def covered(self, x, y, h, margin, skip=None):
        for seg in self.cells.get((int(math.floor(x / self.cell)), int(math.floor(y / self.cell))), ()):
            if seg[6] == skip or abs(seg[5] - h) > 2.5:
                continue
            if seg_distance(x, y, *seg[:4]) < seg[4] + margin:
                return True
        return False


def point_in_polygon(x, y, poly):
    inside = False
    for (x1, y1), (x2, y2) in zip(poly, poly[1:] + poly[:1]):
        if (y1 > y) != (y2 > y) and x < x1 + (y - y1) * (x2 - x1) / (y2 - y1):
            inside = not inside
    return inside


def road_width(road_type):
    if road_type in ("primary", "trunk", "motorway"):
        return 13
    if road_type in ("secondary", "tertiary"):
        return 8
    return 5


def sidewalk_width(width):
    return 3.5 if width >= 13 else 2.5 if width >= 8 else 1.6


def left_of(d):
    return (-d[1], d[0])


for model_name, source_name, center_lat, center_lon in DISTRICTS:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    root = ET.parse(os.path.join(SOURCES, source_name + "-osm.xml")).getroot()
    nodes = {int(node.get("id")): (float(node.get("lat")), float(node.get("lon"))) for node in root.findall("node")}
    meters_x = 111320 * math.cos(math.radians(center_lat))

    def point(node_id):
        lat, lon = nodes[node_id]
        return ((lon - center_lon) * meters_x, (lat - center_lat) * 111320)

    parent = bpy.data.objects.new(model_name + "_OSM", None)
    bpy.context.collection.objects.link(parent)
    geom = {key: ([], [], []) for key in MATS}
    stats = {"buildings": 0, "roads": 0, "busways": 0, "footways": 0, "bridges": 0, "measured_heights": 0}

    def add_quad(kind, positions, uv=None):
        vertices, faces, uvs = geom[kind]
        base = len(vertices)
        vertices.extend(positions)
        uvs.extend(uv or [(x / 4, y / 4) for x, y, _ in positions])
        faces.append(tuple(range(base, base + len(positions))))

    def add_polygon(kind, points, height, scale=8.0, down=False):
        """Flat filled polygon (roofs, plazas, aprons, platforms) facing up, or down for a soffit."""
        vertices, faces, uvs = geom[kind]
        base = len(vertices)
        vertices.extend((x, y, height) for x, y in points)
        uvs.extend((x / scale, y / scale) for x, y in points)
        try:
            for a, b, c in tessellate_polygon([[Vector((x, y, height)) for x, y in points]]):
                pa, pb, pc = points[a], points[b], points[c]
                if ((pb[0] - pa[0]) * (pc[1] - pa[1]) - (pb[1] - pa[1]) * (pc[0] - pa[0]) < 0) != down:
                    b, c = c, b
                faces.append((base + a, base + b, base + c))
        except Exception:
            for i in range(1, len(points) - 1):
                faces.append((base, base + i + 1, base + i) if down else (base, base + i, base + i + 1))

    def strip(kind, a, b, width, za, zb, road_run=None):
        """Quad of `width` along a -> b (2D points) at heights za, zb; clipped to the tile."""
        (x1, y1), (x2, y2) = a, b
        dx, dy = x2 - x1, y2 - y1
        length = math.hypot(dx, dy)
        if length < 0.05:
            return
        nx, ny = -dy / length * width / 2, dx / length * width / 2
        quad = [(x1 + nx, y1 + ny, za), (x1 - nx, y1 - ny, za), (x2 - nx, y2 - ny, zb), (x2 + nx, y2 + ny, zb)]
        clipped = clip_polygon(quad)
        if len(clipped) < 3:
            return
        if road_run is None:
            add_quad(kind, clipped)
            return
        uv = []
        for px, py, _ in clipped:
            along = ((px - x1) * dx + (py - y1) * dy) / length
            across = ((px - x1) * (-dy) + (py - y1) * dx) / (length * width) + .5
            uv.append(((road_run + along) / ROAD_REPEAT, across))
        add_quad(kind, clipped, uv)

    def median_bus_lane(refs, pts, heights, width):
        """Red bus lane along the left (median) edge of a one-way carriageway, and its bus-only graph line."""
        offset = width / 2 - BUS_LANE_WIDTH / 2
        lane = []
        for i, (x, y) in enumerate(pts):
            nx = ny = 0.0
            for j in (i - 1, i):  # mitred normal from the segments on either side
                if 0 <= j < len(pts) - 1:
                    dx, dy = pts[j + 1][0] - pts[j][0], pts[j + 1][1] - pts[j][1]
                    length = math.hypot(dx, dy) or 1
                    nx, ny = nx - dy / length, ny + dx / length
            norm = math.hypot(nx, ny) or 1
            lane.append((x + nx / norm * offset, y + ny / norm * offset))
        for (a, b), ha, hb in zip(zip(lane, lane[1:]), heights, heights[1:]):
            strip("Busway", a, b, BUS_LANE_WIDTH, ha + ROAD_Z + .006, hb + ROAD_Z + .006)
        piece = []
        for ref, (x, y) in zip(refs, lane):
            if abs(x) < CLIP and abs(y) < CLIP:
                h = height_of(ref)
                piece.append(f"{-x:.1f},{-y:.1f}" + (f",{h:.1f}" if h > .05 else ""))
            else:
                if len(piece) > 1:
                    road_lines.append(f"B{BUS_LANE_WIDTH:g}> " + " ".join(piece))
                piece = []
        if len(piece) > 1:
            road_lines.append(f"B{BUS_LANE_WIDTH:g}> " + " ".join(piece))
        stats["median_bus_lanes"] = stats.get("median_bus_lanes", 0) + 1

    def wall(kind, a, b, top_a, top_b, bottom_a, bottom_b):
        """Vertical face between a and b, visible from both sides."""
        (x1, y1), (x2, y2) = a, b
        if max(abs(x1), abs(y1), abs(x2), abs(y2)) > CLIP:
            return
        add_quad(kind, [(x1, y1, bottom_a), (x2, y2, bottom_b), (x2, y2, top_b), (x1, y1, top_a)])
        add_quad(kind, [(x2, y2, bottom_b), (x1, y1, bottom_a), (x1, y1, top_a), (x2, y2, top_b)])

    def bridge_structure(pts, heights, width):
        """Fascia, railings, soffit and piers below an elevated deck."""
        run = 0.0
        for (a, b), ha, hb in zip(zip(pts, pts[1:]), heights, heights[1:]):
            dx, dy = b[0] - a[0], b[1] - a[1]
            length = math.hypot(dx, dy)
            if length < 0.05:
                continue
            if max(ha, hb) >= 0.6:
                ux, uy = dx / length, dy / length
                nx, ny = -uy * width / 2, ux * width / 2
                low_a, low_b = max(0, ha - 1.1), max(0, hb - 1.1)
                for s in (1, -1):
                    ea, eb = (a[0] + nx * s, a[1] + ny * s), (b[0] + nx * s, b[1] + ny * s)
                    wall("Bridge", ea, eb, ha, hb, low_a, low_b)
                    wall("Railing", ea, eb, ha + 1.1, hb + 1.1, ha + .9, hb + .9)
                add_quad("Bridge", [(a[0] - nx, a[1] - ny, low_a), (a[0] + nx, a[1] + ny, low_a),
                                    (b[0] + nx, b[1] + ny, low_b), (b[0] - nx, b[1] - ny, low_b)])
                k = PIER_SPACING - run % PIER_SPACING
                while k < length:
                    h = ha + (hb - ha) * k / length
                    if h > 2.5:
                        cx, cy = a[0] + ux * k, a[1] + uy * k
                        pw = min(width * .35, 2.2)
                        corners = [(cx + nx / (width / 2) * pw * sx + ux * .6 * sy, cy + ny / (width / 2) * pw * sx + uy * .6 * sy)
                                   for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
                        for p1, p2 in zip(corners, corners[1:] + corners[:1]):
                            wall("Bridge", p1, p2, h - 1.1, h - 1.1, 0, 0)
                    k += PIER_SPACING
            run += length

    # ---------- collect ways ----------
    ways = []  # (id, tags, refs)
    for way in root.findall("way"):
        tags = {tag.get("k"): tag.get("v") for tag in way.findall("tag")}
        refs = [int(nd.get("ref")) for nd in way.findall("nd") if int(nd.get("ref")) in nodes]
        if len(refs) >= 2:
            ways.append((int(way.get("id")), tags, refs))
    way_refs = {wid: refs for wid, _, refs in ways}

    def visible(refs):
        return any(abs(x) < 280 and abs(y) < 280 for x, y in map(point, refs))

    def structure_beam(a,b,width=.22):
        a,b=Vector(a),Vector(b);along=(b-a).normalized()
        side=along.cross(Vector((0,0,1)))
        if side.length<.01:side=Vector((1,0,0))
        side.normalize();up=along.cross(side).normalized()
        ring=[side*width*sx/2+up*width*sy/2 for sx,sy in ((-1,-1),(1,-1),(1,1),(-1,1))]
        for k in range(4):add_quad("FacadeSteel",[tuple(a+ring[k]),tuple(a+ring[(k+1)%4]),tuple(b+ring[(k+1)%4]),tuple(b+ring[k])])

    # Buildings: closed ways plus outer rings of multipolygon relations.
    building_shapes = [(wid, tags, refs) for wid, tags, refs in ways if "building" in tags and refs[0] == refs[-1]]
    for relation in root.findall("relation"):
        tags = {tag.get("k"): tag.get("v") for tag in relation.findall("tag")}
        if tags.get("type") != "multipolygon" or "building" not in tags:
            continue
        outers = [way_refs[int(m.get("ref"))] for m in relation.findall("member")
                  if m.get("type") == "way" and m.get("role") in ("outer", "") and int(m.get("ref")) in way_refs]
        for ring in join_rings(outers):
            building_shapes.append((int(relation.get("id")), tags, ring))
    # Surface track nodes: a station building drawn over them is a train shed, not a closed box.
    rail_points = [point(r) for _, tags, refs in ways
                   if tags.get("railway") in ("rail", "subway", "light_rail", "narrow_gauge")
                   and tags.get("tunnel") not in ("yes", "building_passage") and layer_of(tags) > -2 for r in refs]
    # Keep detail on exposed faces. Party walls and artificial tile-cut faces stay plain.
    all_footprints = []
    for building_id, _, ring in building_shapes:
        if model_name=="Hongdae" and building_id==608075859: continue
        poly = [point(r) for r in ring[:-1]]
        if len(poly)>2:
            all_footprints.append((min(x for x,y in poly),max(x for x,y in poly),
                                   min(y for x,y in poly),max(y for x,y in poly),poly))
    street_segments = [(point(a),point(b)) for _,tags,refs in ways
                       if tags.get("highway") in CAR_TYPES | WALK_TYPES and tags.get("tunnel")!="yes" and layer_of(tags)>=0
                       for a,b in zip(refs,refs[1:])]

    def near_street(x,y):
        best=1e9
        for a,b in street_segments:
            dx,dy=b[0]-a[0],b[1]-a[1]; den=dx*dx+dy*dy
            if den<.01: continue
            t=max(0,min(1,((x-a[0])*dx+(y-a[1])*dy)/den))
            best=min(best,math.hypot(x-a[0]-dx*t,y-a[1]-dy*t))
        return best

    def facade_details(a,b,pts,height,wid,transit,base=0.15):
        length=math.dist(a,b)
        if length<3 or length>650: return
        ux,uy=(b[0]-a[0])/length,(b[1]-a[1])/length
        nx,ny=-uy,ux
        mx,my=(a[0]+b[0])/2,(a[1]+b[1])/2
        if point_in_polygon(mx+nx*.2,my+ny*.2,pts): nx,ny=-nx,-ny
        if max(abs(mx),abs(my))>=CLIP-.1: return
        px,py=mx+nx*.55,my+ny*.55
        if any(x0<px<x1 and y0<py<y1 and point_in_polygon(px,py,poly)
               for x0,x1,y0,y1,poly in all_footprints): return
        def v(u,z,d):return (a[0]+ux*u+nx*d,a[1]+uy*u+ny*d,z)
        def box(kind,u,z,w,h,depth=.1,offset=.04):
            # Closed, shallow boxes: real sills, mullions and signage instead of flat decals.
            q=[v(u-w/2,z-h/2,offset),v(u+w/2,z-h/2,offset),
               v(u+w/2,z+h/2,offset),v(u-w/2,z+h/2,offset)]
            r=[v(u-w/2,z-h/2,offset+depth),v(u+w/2,z-h/2,offset+depth),
               v(u+w/2,z+h/2,offset+depth),v(u-w/2,z+h/2,offset+depth)]
            # Winding depends on the footprint ring direction.
            faces=[r] if kind in ("FacadeGlazing","FacadeGlazingLight","FacadeShop") or z>15 else [r]+[[q[k],r[k],r[(k+1)%4],q[(k+1)%4]] for k in range(4)]
            outward=Vector(r[1])-Vector(r[0]); up=Vector(r[2])-Vector(r[1])
            reverse=outward.cross(up).dot(Vector((nx,ny,0)))<0
            for face in faces: add_quad(kind,list(reversed(face)) if reverse else face)
        glass_building=transit or height>=35
        bay=2.6 if transit else 3.2
        bays=max(1,int(length/bay)); pitch=length/bays
        floors=max(1,int((height-base)/3.2)); floor_h=(height-base)/floors
        street=near_street(px,py)<24
        for floor in range(floors):
            z=base+(floor+.5)*floor_h
            box("FacadeTrim",length/2,base+floor*floor_h,length,.09,.14)
            for j in range(bays):
                u=(j+.5)*pitch
                is_shop=floor==0 and base<1 and street and not transit
                w=pitch-(.14 if glass_building else .50)
                h=floor_h-(.18 if glass_building else .85)
                if is_shop: w=pitch-.18; h=floor_h-.48
                kind="FacadeShop" if is_shop else "FacadeGlazingLight" if (j+floor+wid)%5==0 else "FacadeGlazing"
                box("FacadeFrame",u,z,w+.12,h+.12,.08)
                box(kind,u,z,w,h,.015,.13)
                box("FacadeFrame",u,z,.055,h,.035,.15)
                if not glass_building: box("FacadeTrim",u,z-h*.5-.09,w+.22,.12,.24)
                if is_shop:
                    box("FacadeSign",u,base+floor_h-.12,pitch-.12,.38,.18)
                    # Roadview: Hongdae low-rise retail has fabric awnings above the shop windows.
                    if model_name=="Hongdae" and height<22 and (j+wid)%3!=0:
                        kind="FacadeAwningRed" if wid%2 else "FacadeAwningCream"
                        add_quad(kind,[v(u-w/2,2.95,.2),v(u+w/2,2.95,.2),v(u+w/2,2.62,.95),v(u-w/2,2.62,.95)])
                        box(kind,u,2.55,w,.15,.06,.91)
                stats["window_bays"]=stats.get("window_bays",0)+1
        box("FacadeTrim",length/2,height+.1,length,.24,.24)
        # Gimpo's landside (NE) glass frontage has a wide roof over the drop-off carriageway.
        # Dimensions are visual estimates; retain the surveyed OSM footprint and road network.
        if transit and model_name=="GimpoAirport" and nx+ny>1.2 and length>18:
            depth=11
            for j in range(max(1,int(length/3))):
                u0=j*length/max(1,int(length/3));u1=(j+1)*length/max(1,int(length/3))
                add_quad("FacadeSoffit",[v(u0,height-.1,0),v(u1,height-.1,0),v(u1,height+.8,depth),v(u0,height+.8,depth)])
                add_quad("FacadeSoffit",[v(u0,height-.24,0),v(u0,height+.66,depth),v(u1,height+.66,depth),v(u1,height-.24,0)])
                for d in (0,depth):box("FacadeSteel",(u0+u1)/2,height+.8*d/depth,u1-u0,.18,.18,d)
            for j in range(max(1,int(length/10))):
                u=(j+.5)*length/max(1,int(length/10))
                box("FacadeSteel",u,height*.5,.28,height,.28,depth-.14)
            stats["airport_canopy_faces"]=stats.get("airport_canopy_faces",0)+1

    footprints = []
    for wid, tags, refs in building_shapes:
        # Hongdae exit 3 shelter, visually checked in June 2026 Roadview. Runtime builds the open stair canopy.
        if model_name=="Hongdae" and wid==608075859:
            stats["entrance_shelters_excluded"]=stats.get("entrance_shelters_excluded",0)+1
            continue
        if len(refs) < 4 or not visible(refs) or tags.get("building") == "roof":
            continue
        if tags.get("location") == "underground" or tags.get("tunnel") == "yes" or tags.get("layer", "0").startswith("-"):
            continue
        pts = clip_polygon([point(r) for r in refs[:-1]])
        if len(pts) < 3 or len(pts) > 400:
            continue
        height = None
        try:
            height = float(tags.get("height", "").replace(" m", "").replace("m", ""))
        except ValueError:
            pass
        transit = tags.get("building") in ("train_station", "transportation", "terminal") or tags.get("aeroway") == "terminal"
        if height is None:
            try:
                height = float(tags["building:levels"]) * 3.2
            except (KeyError, ValueError):
                height = 18 if transit else 11 if model_name == "Gangnam" else 9
        else:
            stats["measured_heights"] += 1
        height = max(2.5, min(190, height))
        x0, x1, y0, y1 = min(x for x, _ in pts), max(x for x, _ in pts), min(y for _, y in pts), max(y for _, y in pts)
        if transit and any(x0 < x < x1 and y0 < y < y1 and point_in_polygon(x, y, pts) for x, y in rail_points):
            # Train shed: a roof with a glass fascia over the tracks, open below so trains and platforms stay clear.
            roof = max(12.0, height)
            add_polygon("Roof", pts, roof)
            add_polygon("Canopy", pts, roof - .05, 6, down=True)
            for a, b in zip(pts, pts[1:] + pts[:1]):
                wall("BuildingGlass", a, b, roof, roof, roof - 4.0, roof - 4.0)
                facade_details(a,b,pts,roof,wid,True,roof-4.0)
                # Projecting steel eave, kept above the open train shed and platforms.
                length=math.dist(a,b)
                if length>12:
                    ux,uy=(b[0]-a[0])/length,(b[1]-a[1])/length
                    nx,ny=-uy,ux;mx,my=(a[0]+b[0])/2,(a[1]+b[1])/2
                    if point_in_polygon(mx+nx*.2,my+ny*.2,pts):nx,ny=-nx,-ny
                    for k in range(max(1,int(length/12))):
                        t=(k+.5)/max(1,int(length/12));px=a[0]+(b[0]-a[0])*t;py=a[1]+(b[1]-a[1])*t
                        if not any(math.hypot(px-rx,py-ry)<2.5 for rx,ry in rail_points):
                            structure_beam((px,py,.15),(px,py,roof+.55),.28)
                            structure_beam((px,py,roof-2),(px+nx*3.7,py+ny*3.7,roof+.25),.18)
                    for j in range(8):
                        d0,d1=j*.5,(j+1)*.5
                        z0,z1=roof+.7-.025*d0*d0,roof+.7-.025*d1*d1
                        add_quad("FacadeSoffit",[(a[0]+nx*d0,a[1]+ny*d0,z0),(b[0]+nx*d0,b[1]+ny*d0,z0),
                                                  (b[0]+nx*d1,b[1]+ny*d1,z1),(a[0]+nx*d1,a[1]+ny*d1,z1)])
                        add_quad("FacadeSoffit",[(a[0]+nx*d1,a[1]+ny*d1,z1-.12),(b[0]+nx*d1,b[1]+ny*d1,z1-.12),
                                                  (b[0]+nx*d0,b[1]+ny*d0,z0-.12),(a[0]+nx*d0,a[1]+ny*d0,z0-.12)])
            stats["train_sheds"] = stats.get("train_sheds", 0) + 1
            continue
        run = 0.0
        style = "BuildingGlass" if transit else facade_style(wid, height)  # stations and terminals: glass curtain walls
        repeat_u, repeat_v = BAY_WIDTH * FACADE_BAYS, FLOOR_HEIGHT * FACADE_BAYS
        entrance_anchor=(150.23,146.06)
        for i,a in enumerate(pts):
            b=pts[(i+1)%len(pts)];length=math.dist(a,b)
            if length<.01:continue
            ux,uy=(b[0]-a[0])/length,(b[1]-a[1])/length
            along=(entrance_anchor[0]-a[0])*ux+(entrance_anchor[1]-a[1])*uy
            distance=abs((entrance_anchor[0]-a[0])*uy-(entrance_anchor[1]-a[1])*ux)
            # The office podium has a walk-through opening beside exit 3 in Roadview.
            passage=model_name=="Hongdae" and wid==690860920 and distance<12 and -3<along<length+3
            spans=[(0,length,.12)]
            if passage:
                lo=max(0,along-4.5);hi=min(length,along+4.5)
                spans=[(0,lo,.12),(lo,hi,4.5),(hi,length,.12)]
                stats["surveyed_passage_faces"]=stats.get("surveyed_passage_faces",0)+1
            for lo,hi,bottom in spans:
                if hi-lo<.1:continue
                aa=(a[0]+ux*lo,a[1]+uy*lo);bb=(a[0]+ux*hi,a[1]+uy*hi)
                u0,u1=(run+lo)/repeat_u,(run+hi)/repeat_u
                add_quad(style,[(aa[0],aa[1],bottom),(bb[0],bb[1],bottom),(bb[0],bb[1],height),(aa[0],aa[1],height)],
                         [(u0,bottom/repeat_v),(u1,bottom/repeat_v),(u1,height/repeat_v),(u0,height/repeat_v)])
                facade_details(aa,bb,pts,height,wid,transit,bottom)
                if bottom>1: add_quad("FacadeSoffit",[(aa[0],aa[1],bottom),(bb[0],bb[1],bottom),
                    (bb[0]-uy*4,bb[1]+ux*4,bottom),(aa[0]-uy*4,aa[1]+ux*4,bottom)])
            run+=length
        add_polygon("Roof", pts, height)
        footprints.append((min(x for x, _ in pts), max(x for x, _ in pts), min(y for _, y in pts), max(y for _, y in pts), pts))
        stats["buildings"] += 1

    # Highway classes. Tunnels and indoor corridors are skipped: they are below or inside other things.
    roads = []
    for wid, tags, refs in ways:
        road_type = tags.get("highway")
        if road_type is None or not visible(refs) or underground(tags) or tags.get("aeroway") == "jet_bridge":
            continue
        if tags.get("area") == "yes":
            if road_type in WALK_TYPES and refs[0] == refs[-1]:
                plaza = clip_polygon([point(r) for r in refs[:-1]])
                if len(plaza) >= 3:
                    add_polygon("Footway", plaza, FOOTWAY_Z - .01, 4)
            continue
        base = road_type[:-5] if road_type.endswith("_link") else road_type
        if base in CAR_TYPES:
            traffic = (tags.get("access") not in NO_CARS and tags.get("motor_vehicle") not in NO_CARS
                       and tags.get("motorcar") not in NO_CARS and tags.get("service") != "parking_aisle")
            # Cars keep to a one-way carriageway's direction (dual carriageways are mapped as two one-way ways).
            oneway = tags.get("oneway") in ("yes", "true", "1", "-1")
            if tags.get("oneway") == "-1":
                refs = list(reversed(refs))
            median = oneway and base in ("primary", "trunk") and tags.get("name") in MEDIAN_BUS_ROADS.get(source_name, ())
            roads.append(dict(kind="Road", tags=tags, refs=refs, width=MEDIAN_ROAD_WIDTH if median else road_width(road_type),
                              traffic=traffic, oneway=oneway, median=median))
        elif road_type in ("busway", "bus_guideway"):
            roads.append(dict(kind="Busway", tags=tags, refs=refs, width=4.0, traffic=True,
                              oneway=tags.get("oneway", "yes" if road_type == "busway" else "no") == "yes"))
        elif road_type in WALK_TYPES:
            roads.append(dict(kind="Footway", tags=tags, refs=refs, width=4.0 if road_type == "pedestrian" else 2.4,
                              traffic=False, oneway=False))
        elif road_type == "platform":
            roads.append(dict(kind="Platform", tags=tags, refs=refs, width=3.0, traffic=False, oneway=False))

    # Node heights: elevated ways ramp up from where they meet ground ways.
    ground_nodes = set()
    for road in roads:
        if not elevated(road["tags"]):
            ground_nodes.update(road["refs"])
    elevated_uses = {}
    for road in roads:
        if elevated(road["tags"]):
            for ref in set(road["refs"]):
                elevated_uses[ref] = elevated_uses.get(ref, 0) + 1
    node_height = {}
    for road in roads:
        if not elevated(road["tags"]):
            continue
        refs = road["refs"]
        deck = LAYER_HEIGHT * max(1, layer_of(road["tags"]))
        pts = [point(r) for r in refs]
        dist = [0.0]
        for a, b in zip(pts, pts[1:]):
            dist.append(dist[-1] + math.hypot(b[0] - a[0], b[1] - a[1]))
        total = dist[-1]
        ramp = max(1.0, min(RAMP, total / 2))
        # Ends meeting a ground way ramp down; so do ends that stop in mid-air inside the tile.
        start_low = refs[0] in ground_nodes or (elevated_uses.get(refs[0], 0) < 2 and max(map(abs, pts[0])) < CLIP - 5)
        end_low = refs[-1] in ground_nodes or (elevated_uses.get(refs[-1], 0) < 2 and max(map(abs, pts[-1])) < CLIP - 5)
        for ref, s in zip(refs, dist):
            f = 1.0
            if start_low:
                f = min(f, s / ramp)
            if end_low:
                f = min(f, (total - s) / ramp)
            node_height[ref] = max(node_height.get(ref, 0.0), deck * max(0.0, f))
        stats["bridges"] += 1

    def height_of(ref):
        return node_height.get(ref, 0.0)

    # Surfaces and the car/bus graph.
    road_lines = []
    car_links = {}  # node -> {neighbour: width}
    road_owner = {}
    grid = SegmentGrid()
    for index, road in enumerate(roads):
        kind, refs, width = road["kind"], road["refs"], road["width"]
        pts = [point(r) for r in refs]
        heights = [height_of(r) for r in refs]
        lift = {"Road": ROAD_Z, "Busway": ROAD_Z + .004, "Footway": FOOTWAY_Z, "Platform": .3}[kind]
        run = 0.0
        for (a, b), ha, hb in zip(zip(pts, pts[1:]), heights, heights[1:]):
            length = math.hypot(b[0] - a[0], b[1] - a[1])
            if length < 0.2:
                continue
            if kind in ("Road", "Busway"):
                strip(kind, a, b, width, ha + lift, hb + lift, run)
                grid.add((a[0], a[1], b[0], b[1], width / 2, (ha + hb) / 2, index))
            elif kind == "Platform":
                strip("Platform", a, b, width, ha + lift, hb + lift)
                ux, uy = (b[0] - a[0]) / length, (b[1] - a[1]) / length
                nx, ny = -uy * width / 2, ux * width / 2
                for s in (1, -1):
                    wall("Curb", (a[0] + nx * s, a[1] + ny * s), (b[0] + nx * s, b[1] + ny * s), ha + lift, hb + lift, ha, hb)
            else:
                strip("Footway", a, b, width, ha + lift, hb + lift)
            run += length
        if elevated(road["tags"]):
            bridge_structure(pts, heights, width)
        if kind in ("Road", "Busway"):
            stats["roads" if kind == "Road" else "busways"] += 1
        elif kind == "Footway":
            stats["footways"] += 1
        if road.get("median"):
            median_bus_lane(refs, pts, heights, width)
        if kind in ("Road", "Busway") and road["traffic"]:
            flags = ("B" if kind == "Busway" else "") + f"{width:g}" + (">" if road["oneway"] else "")
            piece = []
            for ref, (x, y) in zip(refs, pts):
                if abs(x) < CLIP and abs(y) < CLIP:
                    h = height_of(ref)
                    piece.append(f"{-x:.1f},{-y:.1f}" + (f",{h:.1f}" if h > .05 else ""))
                else:
                    if len(piece) > 1:
                        road_lines.append(flags + " " + " ".join(piece))
                    piece = []
            if len(piece) > 1:
                road_lines.append(flags + " " + " ".join(piece))
        if kind == "Road":
            for a, b in zip(refs, refs[1:]):
                if a != b:
                    car_links.setdefault(a, {})[b] = width
                    car_links.setdefault(b, {})[a] = width
                    road_owner[(a, b)] = road_owner[(b, a)] = index

    # Junctions: plain asphalt patch over crossing lane paint, zebra crossings on each approach.
    junctions = 0
    junction_radius = {}
    for node_id, links in car_links.items():
        if len(links) < 2:
            continue
        cx, cy = point(node_id)
        unique = list(links.items())
        radius = max(width for _, width in unique) / 2 + 0.6
        junction_radius[node_id] = radius
        if abs(cx) > 300 or abs(cy) > 300 or height_of(node_id) > .5:
            continue
        if len(unique) == 2:
            a = Vector(point(unique[0][0])) - Vector((cx, cy))
            b = Vector(point(unique[1][0])) - Vector((cx, cy))
            if a.length and b.length and a.normalized().dot(b.normalized()) < -.995:
                continue
        corners = []
        for other, width in unique:
            ox, oy = point(other)
            length = math.hypot(ox - cx, oy - cy)
            if length < .1:
                continue
            dx, dy = (ox - cx) / length, (oy - cy) / length
            nx, ny = -dy * width / 2, dx * width / 2
            corners.extend([(cx + dx * radius + nx, cy + dy * radius + ny), (cx + dx * radius - nx, cy + dy * radius - ny)])
        corners.sort(key=lambda p: math.atan2(p[1] - cy, p[0] - cx))

        def cross(o, a, b):
            return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
        lower = []
        for p in corners:
            while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
                lower.pop()
            lower.append(p)
        upper = []
        for p in reversed(corners):
            while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
                upper.pop()
            upper.append(p)
        ring = [(x, y, .062) for x, y in lower[:-1] + upper[:-1]]
        if len(ring) >= 3:
            add_quad("Junction", ring)
        if len(unique) < 3:
            continue
        for other, width in unique:
            ox, oy = point(other)
            length = math.hypot(ox - cx, oy - cy)
            if length < radius + 4:
                continue
            dx, dy = (ox - cx) / length, (oy - cy) / length
            px, py = -dy, dx
            mx, my = cx + dx * (radius + 1.3), cy + dy * (radius + 1.3)
            stripes = max(2, int(width / 0.9))
            for k in range(stripes):
                off = -width / 2 + (k + 0.5) * width / stripes
                sx, sy = mx + px * off, my + py * off
                hw, hl = 0.25, 1.2
                # Counter-clockwise seen from above so the stripe faces up.
                add_quad("Crosswalk", [(sx - px * hw + dx * hl, sy - py * hw + dy * hl, 0.064),
                                       (sx + px * hw + dx * hl, sy + py * hw + dy * hl, 0.064),
                                       (sx + px * hw - dx * hl, sy + py * hw - dy * hl, 0.064),
                                       (sx - px * hw - dx * hl, sy - py * hw - dy * hl, 0.064)])
        junctions += 1
    stats["junctions"] = junctions

    # Sidewalk network. Each car-road node gets a kerb-side point on both sides of every arm:
    # trimmed back to the zebra crossing at junctions, mitred at bends. Sidewalks join round block
    # corners and cross each arm on its zebra, so people never walk along a carriageway.
    arm_points = {}  # (node, neighbour) -> (left point, right point) looking from node to neighbour
    for node_id, links in car_links.items():
        cx, cy = point(node_id)
        arms = []
        for other, width in links.items():
            ox, oy = point(other)
            length = math.hypot(ox - cx, oy - cy)
            if length < .1:
                continue
            d = ((ox - cx) / length, (oy - cy) / length)
            arms.append((math.atan2(d[1], d[0]), other, d, width / 2 + sidewalk_width(width) / 2, length))
        arms.sort()
        if len(arms) >= 3:
            trim = junction_radius.get(node_id, 0) + 1.3
            for _, other, d, off, length in arms:
                r = min(trim, length * .45)
                n = left_of(d)
                arm_points[(node_id, other)] = ((cx + d[0] * r + n[0] * off, cy + d[1] * r + n[1] * off),
                                                (cx + d[0] * r - n[0] * off, cy + d[1] * r - n[1] * off))
        elif len(arms) == 2:
            corner = {}
            for i in range(2):
                (_, o0, d0, off0, _), (_, o1, d1, off1, _) = arms[i], arms[1 - i]
                n0, n1 = left_of(d0), left_of(d1)
                p0 = (cx + n0[0] * off0, cy + n0[1] * off0)
                p1 = (cx - n1[0] * off1, cy - n1[1] * off1)
                det = d0[0] * (-d1[1]) - d0[1] * (-d1[0])
                q = p0
                if abs(det) > 1e-3:
                    t = ((p1[0] - p0[0]) * (-d1[1]) - (p1[1] - p0[1]) * (-d1[0])) / det
                    candidate = (p0[0] + d0[0] * t, p0[1] + d0[1] * t)
                    if math.hypot(candidate[0] - cx, candidate[1] - cy) <= 3 * max(off0, off1):
                        q = candidate
                corner[(o0, "L")] = q
                corner[(o1, "R")] = q
            for _, other, _, _, _ in arms:
                arm_points[(node_id, other)] = (corner[(other, "L")], corner[(other, "R")])
        elif arms:
            _, other, d, off, _ = arms[0]
            n = left_of(d)
            arm_points[(node_id, other)] = ((cx + n[0] * off, cy + n[1] * off), (cx - n[0] * off, cy - n[1] * off))

    def free(p, h, own=None):
        if grid.covered(p[0], p[1], h, .3, own):
            return False
        return not any(x0 - 1 < p[0] < x1 + 1 and y0 - 1 < p[1] < y1 + 1 and point_in_polygon(p[0], p[1], fp)
                       for x0, x1, y0, y1, fp in footprints)

    walk_lines = []

    def coord(p, h):
        return f"{-p[0]:.1f},{-p[1]:.1f}" + (f",{h:.1f}" if h > .05 else "")

    def walk_edge(a, b, ha, hb, own=None, draw=None):
        """Adds a sidewalk edge when it stays clear of carriageways and buildings."""
        if max(abs(a[0]), abs(a[1]), abs(b[0]), abs(b[1])) > CLIP - 2 or math.hypot(b[0] - a[0], b[1] - a[1]) < .2:
            return
        for t in (.2, .5, .8):
            if not free((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t), ha + (hb - ha) * t, own):
                return
        if draw:
            strip("Sidewalk", a, b, draw, ha + SIDEWALK_Z, hb + SIDEWALK_Z)
        walk_lines.append("0 " + coord(a, ha) + " " + coord(b, hb))

    done = set()
    for node_id, links in car_links.items():
        for other, width in links.items():
            if (other, node_id) in done or (node_id, other) not in arm_points or (other, node_id) not in arm_points:
                continue
            done.add((node_id, other))
            ha, hb = height_of(node_id), height_of(other)
            here_l, here_r = arm_points[(node_id, other)]
            there_l, there_r = arm_points[(other, node_id)]
            own = road_owner.get((node_id, other))
            walk_edge(here_l, there_r, ha, hb, own, sidewalk_width(width))
            walk_edge(here_r, there_l, ha, hb, own, sidewalk_width(width))
    for node_id, links in car_links.items():
        if len(links) < 3 or height_of(node_id) > .5:
            continue
        cx, cy = point(node_id)
        arms = sorted((math.atan2(point(o)[1] - cy, point(o)[0] - cx), o) for o in links if (node_id, o) in arm_points)
        for i, (_, other) in enumerate(arms):
            left, right = arm_points[(node_id, other)]
            if max(abs(left[0]), abs(left[1]), abs(right[0]), abs(right[1])) < CLIP - 2:
                walk_lines.append("0 " + coord(left, 0) + " " + coord(right, 0))  # along the zebra
            walk_edge(left, arm_points[(node_id, arms[(i + 1) % len(arms)][1])][1], 0, 0, None, 2.0)

    # Footways become walk lines too, cut where they run onto carriageways.
    for road in roads:
        if road["kind"] not in ("Footway", "Platform") or road["tags"].get("access") in NO_CARS:
            continue
        piece = []
        for ref in road["refs"]:
            x, y = point(ref)
            h = height_of(ref)
            if abs(x) < CLIP - 2 and abs(y) < CLIP - 2 and not grid.covered(x, y, h, .4):
                piece.append(coord((x, y), h))
            else:
                if len(piece) > 1:
                    walk_lines.append("0 " + " ".join(piece))
                piece = []
        if len(piece) > 1:
            walk_lines.append("0 " + " ".join(piece))

    # Surface railways: ballast, sleepers and rails. Seoul Station's main line sits in a cutting under
    # the decks (OSM layer -1 without tunnel) and is drawn at ground level in this flat tile.
    for wid, tags, refs in ways:
        if tags.get("railway") not in ("rail", "subway", "light_rail", "narrow_gauge") or not visible(refs):
            continue
        if tags.get("tunnel") in ("yes", "building_passage") or layer_of(tags) <= -2:
            continue
        pts = [point(r) for r in refs]
        for a, b in zip(pts, pts[1:]):
            length = math.hypot(b[0] - a[0], b[1] - a[1])
            if length < .1:
                continue
            strip("Ballast", a, b, 3.4, .045, .045)
            ux, uy = (b[0] - a[0]) / length, (b[1] - a[1]) / length
            k = .3
            while k < length:
                c = (a[0] + ux * k, a[1] + uy * k)
                strip("Sleeper", (c[0] - ux * .12, c[1] - uy * .12), (c[0] + ux * .12, c[1] + uy * .12), 2.5, .1, .1)
                k += .65
            for s in (.72, -.72):
                o = (-uy * s, ux * s)
                ra, rb = (a[0] + o[0], a[1] + o[1]), (b[0] + o[0], b[1] + o[1])
                strip("Rail", ra, rb, .1, .19, .19)
                wall("Rail", ra, rb, .19, .19, .1, .1)

    # Rail platforms (raised 1.1 m, yellow safety edge, canopy) and the airport apron and taxiways.
    for wid, tags, refs in ways:
        if not visible(refs):
            continue
        if tags.get("railway") == "platform" and not underground(tags):
            pts = [point(r) for r in refs]
            if refs[0] == refs[-1] and len(refs) >= 4:
                ring = clip_polygon(pts[:-1])
                if len(ring) >= 3:
                    add_polygon("Platform", ring, 1.1, 4)
                    add_polygon("Canopy", ring, 6.5, 6)
                    for a, b in zip(ring, ring[1:] + ring[:1]):
                        wall("PlatformEdge", a, b, 1.105, 1.105, 1.0, 1.0)
                        wall("Platform", a, b, 1.0, 1.0, 0, 0)
            else:
                for a, b in zip(pts, pts[1:]):
                    length = math.hypot(b[0] - a[0], b[1] - a[1])
                    if length < .1:
                        continue
                    strip("Platform", a, b, 6.0, 1.1, 1.1)
                    strip("Canopy", a, b, 7.0, 6.5, 6.5)
                    nx, ny = -(b[1] - a[1]) / length * 3, (b[0] - a[0]) / length * 3
                    for s in (1, -1):
                        wall("Platform", (a[0] + nx * s, a[1] + ny * s), (b[0] + nx * s, b[1] + ny * s), 1.0, 1.0, 0, 0)
                        strip("PlatformEdge", (a[0] + nx * s * .9, a[1] + ny * s * .9),
                              (b[0] + nx * s * .9, b[1] + ny * s * .9), .45, 1.105, 1.105)
        elif tags.get("aeroway") == "apron" and refs[0] == refs[-1]:
            ring = clip_polygon([point(r) for r in refs[:-1]])
            if len(ring) >= 3:
                add_polygon("Apron", ring, .03, 10)
        elif tags.get("aeroway") == "taxiway":
            pts = [point(r) for r in refs]
            for a, b in zip(pts, pts[1:]):
                strip("Taxiway", a, b, 23, .035, .035)
                strip("TaxiLine", a, b, .35, .045, .045)

    add_quad("Ground", [(-320, -320, 0), (320, -320, 0), (320, 320, 0), (-320, 320, 0)])
    for kind, (vertices, faces, uvs) in geom.items():
        mesh_object(kind, vertices, faces, uvs, parent)
    with open(os.path.join(GEO_OUT, model_name + "Roads.txt"), "w") as handle:
        handle.write("# [B]width[>] x,z[,y] ... car and bus lane centre lines in Unity metres; B bus only, > one way. "
                     "OSM data (c) OpenStreetMap contributors, ODbL\n")
        handle.write("\n".join(road_lines) + "\n")
    with open(os.path.join(GEO_OUT, model_name + "Walks.txt"), "w") as handle:
        handle.write("# 0 x,z[,y] ... pedestrian network: sidewalks, zebra crossings, footways. "
                     "OSM data (c) OpenStreetMap contributors, ODbL\n")
        handle.write("\n".join(walk_lines) + "\n")
    stats["walk_lines"] = len(walk_lines)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in bpy.context.scene.objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parent
    path = os.path.join(OUT, model_name + "OSM.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, axis_forward="-Z", axis_up="Y", add_leaf_bones=False)
    print("DISTRICT", model_name, stats, "export", path)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "AssetSources/" + model_name + "OSM.blend"))
