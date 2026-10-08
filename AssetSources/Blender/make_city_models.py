"""Generate the city-builder models for Unity.

Run: Blender -b --factory-startup -P AssetSources/Blender/make_city_models.py
Writes one FBX per building id plus vehicles, cabins and street props to
PeninsulaTimeUnity/Assets/Resources/Models/City, and saves CityModels.blend.

Conventions: metres, Blender Z up, street-facing facade toward -Y (Unity -Z),
vehicles face +Y (Unity +Z). Building lots fit inside 9.6 x 9.0 m.
"""
import math
import os
import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, "PeninsulaTimeUnity", "Assets", "Resources", "Models", "City")
os.makedirs(OUT, exist_ok=True)

PALETTE = {
    "grass": (.30, .45, .25), "lawn": (.38, .55, .30), "soil": (.36, .26, .17), "crop": (.55, .62, .22),
    "wheat": (.80, .66, .30), "concrete": (.62, .62, .60), "pavement": (.50, .50, .49), "asphalt": (.13, .14, .15),
    "line_white": (.92, .92, .88), "line_yellow": (.93, .72, .18), "plaster": (.86, .82, .72), "white": (.90, .90, .87),
    "brick": (.55, .25, .17), "stone": (.48, .48, .45), "granite": (.40, .42, .42),
    "timber": (.42, .24, .12), "darkwood": (.25, .14, .08), "thatch": (.62, .48, .24), "giwa": (.16, .19, .21),
    "roof_grey": (.30, .32, .34), "glass": (.16, .30, .38),
    "glass_lit": (.95, .80, .48), "metal": (.55, .58, .60), "darkmetal": (.22, .24, .26), "steel_blue": (.24, .40, .58),
    "factory_blue": (.33, .50, .66), "rust": (.52, .30, .18), "coal": (.08, .08, .09), "red": (.72, .15, .12),
    "orange": (.88, .45, .12), "yellow": (.92, .78, .22), "green": (.20, .55, .30),
    "bus_green": (.20, .62, .32), "bus_blue": (.10, .32, .66), "metro_body": (.80, .82, .82), "metro_stripe": (.10, .45, .25),
    "rubber": (.05, .05, .06), "seat_blue": (.20, .30, .52), "seat_red": (.62, .18, .20), "chrome": (.78, .80, .82),
    "leaf": (.24, .45, .22), "leaf_dark": (.17, .34, .18), "bark": (.33, .23, .15), "fabric_red": (.72, .20, .18),
    "fabric_blue": (.18, .38, .66), "fabric_yellow": (.90, .72, .25), "pottery": (.56, .36, .24), "celadon": (.52, .66, .58),
    "fire": (1.0, .55, .15), "cooling": (.78, .78, .75), "dome": (.88, .88, .85), "solar": (.10, .16, .28),
    "light": (1.0, .92, .70), "car_red": (.66, .10, .10), "car_white": (.88, .88, .86), "car_black": (.10, .10, .11),
    "car_silver": (.62, .64, .66), "water": (.18, .38, .48), "sign": (.12, .32, .62),
    "skin": (.86, .68, .54), "hair": (.08, .06, .05), "shirt": (.30, .42, .62), "pants": (.18, .19, .24),
    "shoe": (.10, .09, .08), "door_wood": (.45, .26, .14), "brass": (.80, .62, .25),
    "ktx_white": (.93, .94, .95), "ktx_blue": (.10, .30, .64), "ktx_grey": (.30, .33, .36),
    "plane_white": (.94, .95, .96), "plane_belly": (.68, .71, .74), "tail_blue": (.11, .24, .52),
    "screen": (.10, .42, .58), "reader": (.95, .80, .20),
}
MATS = {}


def material(key):
    if key not in MATS:
        m = bpy.data.materials.new(key)
        rgb = PALETTE[key]
        m.diffuse_color = (*rgb, 1)
        m.use_nodes = True
        bsdf = next((node for node in m.node_tree.nodes if node.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None:
            bsdf = m.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
        bsdf.inputs["Base Color"].default_value = (*rgb, 1)
        bsdf.inputs["Roughness"].default_value = .25 if key.startswith("glass") or key in ("chrome", "water") else .7
        bsdf.inputs["Metallic"].default_value = .6 if key in ("metal", "chrome", "darkmetal", "steel_blue") else 0
        MATS[key] = m
    return MATS[key]


class Model:
    """Collects faces into one bmesh with a material per face."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.keys = []

    def _tag(self, start, key):
        if key not in self.keys:
            self.keys.append(key)
        index = self.keys.index(key)
        self.bm.faces.ensure_lookup_table()
        for face in self.bm.faces[start:]:
            face.material_index = index

    def box(self, key, center, size, rot_z=0.0, rot_x=0.0, rot_y=0.0):
        """Box by centre point (x, y, z) and full size."""
        start = len(self.bm.faces)
        m = (Matrix.Translation(Vector(center)) @ Matrix.Rotation(rot_z, 4, "Z") @ Matrix.Rotation(rot_y, 4, "Y")
             @ Matrix.Rotation(rot_x, 4, "X") @ Matrix.Diagonal((*size, 1)))
        bmesh.ops.create_cube(self.bm, size=1, matrix=m)
        self._tag(start, key)

    def rounded_box(self, key, center, size, radius=.12, rot_z=0.0):
        """Rounded XY corners without a fragile mesh bevel operation."""
        cx, cy, cz = center
        self.box(key, center, (size[0]-2*radius, size[1], size[2]), rot_z)
        self.box(key, center, (size[0], size[1]-2*radius, size[2]), rot_z)
        turn = Matrix.Rotation(rot_z, 3, "Z")
        for sx in (-1, 1):
            for sy in (-1, 1):
                local = Vector((sx*(size[0]/2-radius), sy*(size[1]/2-radius), 0))
                local.rotate(turn)
                self.cylinder(key, (cx+local.x, cy+local.y, cz-size[2]/2), radius, size[2], 8)

    def block(self, key, x0, y0, z0, x1, y1, z1):
        """Box by min/max corners."""
        self.box(key, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)))

    def lathe(self, key, profile, center=(0, 0, 0), segments=24, cap=True, axis="Z", rot_z=0.0):
        """Surface of revolution from (radius, height) pairs."""
        start = len(self.bm.faces)
        cx, cy, cz = center
        rings = []
        for r, h in profile:
            ring = []
            for s in range(segments):
                a = s * math.tau / segments
                p = Vector((math.cos(a) * r, math.sin(a) * r, h))
                if axis == "X":
                    p = Vector((p.z, p.x, p.y))
                elif axis == "Y":
                    p = Vector((p.x, p.z, p.y))
                p.rotate(Matrix.Rotation(rot_z, 3, "Z"))
                ring.append(self.bm.verts.new((p.x + cx, p.y + cy, p.z + cz)))
            rings.append(ring)
        for a, b in zip(rings, rings[1:]):
            for s in range(segments):
                t = (s + 1) % segments
                self.bm.faces.new((a[s], a[t], b[t], b[s]))
        if cap:
            if profile[0][0] > 1e-3:
                self.bm.faces.new(list(reversed(rings[0])))
            if profile[-1][0] > 1e-3:
                self.bm.faces.new(rings[-1])
        self._tag(start, key)

    def cylinder(self, key, center, radius, height, segments=16, axis="Z", rot_z=0.0):
        x, y, z = center
        if axis == "Z":
            self.lathe(key, [(radius, 0), (radius, height)], (x, y, z), segments)
        else:
            self.lathe(key, [(radius, -height / 2), (radius, height / 2)], (x, y, z), segments, axis=axis, rot_z=rot_z)

    def cone(self, key, center, radius, height, segments=16, top=0.0):
        self.lathe(key, [(radius, 0), (max(top, 1e-4), height)], center, segments)

    def plate(self, key, points, thickness):
        """Flat convex plate through 3D outline points, thickened along its normal (wings, fins)."""
        start = len(self.bm.faces)
        pts = [Vector(p) for p in points]
        normal = Vector((0, 0, 0))
        for a, b in zip(pts, pts[1:] + pts[:1]):
            normal += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
        normal = normal.normalized() * thickness / 2
        top = [self.bm.verts.new(p + normal) for p in pts]
        bottom = [self.bm.verts.new(p - normal) for p in pts]
        self.bm.faces.new(top)
        self.bm.faces.new(list(reversed(bottom)))
        for i in range(len(pts)):
            j = (i + 1) % len(pts)
            self.bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
        self._tag(start, key)

    def sphere(self, key, center, radius, subdivisions=2, squash=1.0):
        start = len(self.bm.faces)
        m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((radius, radius, radius * squash, 1))
        bmesh.ops.create_icosphere(self.bm, subdivisions=max(subdivisions,3) if self.name.startswith("Person") or self.name=="Tree" else subdivisions, radius=1, matrix=m)
        self._tag(start, key)
        if self.name.startswith("Person") or self.name=="Tree":
            self.bm.faces.ensure_lookup_table()
            for face in list(self.bm.faces)[start:]:face.smooth=True

    def prism(self, key, x0, x1, y0, y1, z0, ridge_h, overhang=0.3):
        """Gable roof along X with ridge over centre of Y."""
        start = len(self.bm.faces)
        ym = (y0 + y1) / 2
        v = [self.bm.verts.new(p) for p in (
            (x0 - overhang, y0 - overhang, z0), (x1 + overhang, y0 - overhang, z0),
            (x1 + overhang, ym, z0 + ridge_h), (x0 - overhang, ym, z0 + ridge_h),
            (x0 - overhang, y1 + overhang, z0), (x1 + overhang, y1 + overhang, z0))]
        for face in ((v[0], v[1], v[2], v[3]), (v[4], v[3], v[2], v[5]), (v[0], v[3], v[4]), (v[1], v[5], v[2]),
                     (v[0], v[4], v[5], v[1])):
            self.bm.faces.new(face)
        self._tag(start, key)

    def curved_roof(self, key, cx, cy, z0, length, width, height, lift=.45, rot_z=0.0, nu=16, nv=10):
        """Korean giwa roof: concave slopes with upturned eave corners, given thickness."""
        start = len(self.bm.faces)
        grid = []
        for i in range(nu + 1):
            u = -1 + 2 * i / nu
            row = []
            for j in range(nv + 1):
                v = -1 + 2 * j / nv
                z = z0 + height * (1 - abs(v)) ** 1.7 + lift * abs(u) ** 5 * abs(v) + .12 * abs(u) ** 2
                p = Vector((u * length / 2, v * width / 2, z))
                p.rotate(Matrix.Rotation(rot_z, 3, "Z"))
                row.append(self.bm.verts.new((p.x + cx, p.y + cy, p.z)))
            grid.append(row)
        faces = []
        for i in range(nu):
            for j in range(nv):
                faces.append(self.bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1])))
        bmesh.ops.solidify(self.bm, geom=faces, thickness=.16)
        self._tag(start, key)
        self.box(key, (cx, cy, z0 + height + .12), (length + .25, .34, .28), rot_z)
        for end in (-1, 1):
            p = Vector((end * (length / 2 + .05), 0, 0))
            p.rotate(Matrix.Rotation(rot_z, 3, "Z"))
            self.box("white", (cx + p.x, cy + p.y, z0 + height + .14), (.18, .38, .34), rot_z)

    def finish(self):
        mesh = bpy.data.meshes.new(self.name)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces[:])
        self.bm.to_mesh(mesh)
        self.bm.free()
        box_uvs(mesh)
        for key in self.keys:
            mesh.materials.append(material(key))
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        if self.name.startswith("Person") or self.name in ("Bus","BusBlue","BusRed","BusYellow","Metro","Ktx","Car","CarBlue","CarRed","CarWhite","CarBlack"):
            bpy.context.view_layer.objects.active=obj
            obj.select_set(True)
            bevel=obj.modifiers.new("Manufactured edge radii","BEVEL")
            bevel.width=.025
            bevel.segments=3
            bevel.limit_method="ANGLE"
            bpy.ops.object.modifier_apply(modifier=bevel.name)
            obj.select_set(False)
        return obj


TEXTURE_REPEAT = 2.0  # metres per texture tile, matches make_textures.py


def box_uvs(mesh):
    """World-scale box projection so tiled textures keep their size on every face."""
    layer = mesh.uv_layers.new(name="UVMap")
    for poly in mesh.polygons:
        n = poly.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            if az >= ax and az >= ay:
                uv = (co.x, co.y)
            elif ax >= ay:
                uv = (co.y, co.z)
            else:
                uv = (co.x, co.z)
            layer.data[li].uv = (uv[0] / TEXTURE_REPEAT, uv[1] / TEXTURE_REPEAT)


# ---------- shared detail helpers ----------

def lot(m, key="grass", walk=True):
    m.block(key, -4.8, -4.5, 0, 4.8, 4.5, .06)
    if walk:
        m.block("pavement", -4.8, -4.5, .06, 4.8, -3.9, .1)


def tree(m, x, y, s=1.0, dark=False):
    m.lathe("bark", ((.18*s, 0), (.145*s, .65*s), (.115*s, 1.55*s), (.07*s, 2.1*s)),
            (x, y, 0), segments=12)
    for dx, dy, z, tilt in ((-.45, 0, 1.55, -22), (.48, .1, 1.7, 24), (0, -.43, 1.8, -18)):
        m.cylinder("bark", (x+dx*.5*s, y+dy*.5*s, z*.65*s), .065*s, .9*s, 8)
    leaf = "leaf_dark" if dark else "leaf"
    for dx, dy, z, radius in ((0, 0, 2.55, .72), (.56, .08, 2.25, .6),
                              (-.53, -.16, 2.3, .64), (.12, -.57, 2.47, .55),
                              (.15, .55, 2.58, .54), (-.27, .27, 2.91, .52),
                              (.4, -.3, 2.91, .46), (-.45, -.4, 2.73, .46)):
        m.sphere(leaf, (x+dx*s, y+dy*s, z*s), radius*s, 2, .88)


def pine(m, x, y, s=1.0):
    m.cylinder("bark", (x, y, 0), .12 * s, 2.7 * s, 12)
    for i, r in enumerate((1.0, .88, .75, .6, .43)):
        m.cone("leaf_dark" if i%2 else "leaf", (x, y, (.62 + i * .5) * s), r * s, 1.2 * s, 18)


def windows_x(m, y, x0, x1, z0, z1, floors, per, key="glass", depth=.06):
    """Window grid on a facade facing ±Y at plane y."""
    fh = (z1 - z0) / floors
    span = (x1 - x0) / per
    for f in range(floors):
        for k in range(per):
            m.box(key, (x0 + span * (k + .5), y, z0 + fh * (f + .55)), (span * .58, depth, fh * .5))


def windows_y(m, x, y0, y1, z0, z1, floors, per, key="glass", depth=.06):
    fh = (z1 - z0) / floors
    span = (y1 - y0) / per
    for f in range(floors):
        for k in range(per):
            m.box(key, (x, y0 + span * (k + .5), z0 + fh * (f + .55)), (depth, span * .58, fh * .5))


def fence(m, x0, x1, y, h=.9, key="timber", step=.6):
    n = max(1, int(abs(x1 - x0) / step))
    for i in range(n + 1):
        m.box(key, (x0 + (x1 - x0) * i / n, y, h / 2), (.08, .08, h))
    m.box(key, ((x0 + x1) / 2, y, h * .75), (abs(x1 - x0), .05, .07))
    m.box(key, ((x0 + x1) / 2, y, h * .35), (abs(x1 - x0), .05, .07))


def fence_y(m, y0, y1, x, h=.9, key="timber", step=.6):
    n = max(1, int(abs(y1 - y0) / step))
    for i in range(n + 1):
        m.box(key, (x, y0 + (y1 - y0) * i / n, h / 2), (.08, .08, h))
    m.box(key, (x, (y0 + y1) / 2, h * .75), (.05, abs(y1 - y0), .07))


def placer(x, y, z, rot):
    r = Matrix.Rotation(rot, 3, "Z")

    def at(dx, dy, dz):
        p = Vector((dx, dy, dz))
        p.rotate(r)
        return (x + p.x, y + p.y, z + p.z)
    return at


def car(m, x, y, rot=0.0, body="car_red", z=0.0):
    at = placer(x, y, z, rot)
    m.rounded_box(body, at(0, 0, .45), (1.7, 4.2, .6), .16, rot)
    m.rounded_box(body, at(0, -.2, .95), (1.5, 2.2, .5), .12, rot)
    m.box("glass", at(0, .9, .95), (1.4, .05, .42), rot, rot_x=.5)
    m.box("glass", at(0, -1.3, .95), (1.4, .05, .4), rot, rot_x=-.5)
    for sx in (-.86, .86):
        m.box("glass", at(sx, -.2, .97), (.04, 1.9, .36), rot)
        for sy in (-1.35, 1.35):
            m.cylinder("rubber", at(sx, sy, .32), .32, .24, 12, axis="X", rot_z=rot)
    for sx in (-.6, .6):
        m.box("light", at(sx, 2.11, .55), (.35, .03, .14), rot)
        m.box("red", at(sx, -2.11, .55), (.35, .03, .14), rot)


def ac_units(m, xs, y, z):
    for x in xs:
        m.box("metal", (x, y, z + .3), (.9, .7, .6))
        m.cylinder("darkmetal", (x, y, z + .6), .28, .04, 12)


def chimney(m, x, y, base, height, radius, stripes=True):
    m.lathe("concrete", [(radius * 1.2, 0), (radius, height)], (x, y, base), 16)
    if stripes:
        for i in range(2):
            top = height - i * 1.8
            m.lathe("red", [(radius * 1.03, top - .9), (radius * 1.03, top - .3)], (x, y, base), 16, cap=False)
            m.lathe("white", [(radius * 1.03, top - 1.8), (radius * 1.03, top - .9)], (x, y, base), 16, cap=False)


def pylon(m, x, y, h=6.0):
    for dx in (-.4, .4):
        for dy in (-.4, .4):
            m.box("metal", (x + dx * .6, y + dy * .6, h / 2), (.1, .1, h), rot_x=dy * .08, rot_y=-dx * .08)
    for z in (h * .55, h * .8, h):
        m.box("metal", (x, y, z), (2.6, .12, .1))


def vehicle_roof(m, at, key, width, length, edge_z, rise):
    """One arched roof skin; overlapping boxes cause dark z-fighting in exports."""
    start = len(m.bm.faces)
    rows = []
    for yy in (-length / 2, length / 2):
        row = []
        for step in range(9):
            xx = width * (step / 8 - .5)
            zz = edge_z + rise * (1 - (xx / (width / 2)) ** 2)
            row.append(m.bm.verts.new(at(xx, yy, zz)))
        rows.append(row)
    faces = [m.bm.faces.new((rows[0][i], rows[1][i], rows[1][i+1], rows[0][i+1])) for i in range(8)]
    bmesh.ops.solidify(m.bm, geom=faces, thickness=.12)
    m._tag(start, key)


def vehicle_side(m, at, side, length, doors, half_door, body, floor, sill, top, roof, rot=0, stripe=None):
    """Continuous body and framed glazing, with door openings down to the floor."""
    intervals=[]
    start=-length/2
    for door in sorted(doors):
        intervals.append((start,door-half_door))
        start=door+half_door
    intervals.append((start,length/2))
    for a,b in intervals:
        if b<=a:continue
        mid=(a+b)/2
        m.box(body,at(side,mid,(floor+sill)/2),(.08,b-a,sill-floor),rot)
        m.box("rubber",at(side,mid,sill),(.085,b-a,.065),rot)
        m.box(body,at(side,mid,(top+roof)/2),(.08,b-a,roof-top),rot)
        count=max(1,round((b-a)/1.45))
        for index in range(count):
            left=a+(b-a)*index/count
            right=a+(b-a)*(index+1)/count
            m.box("glass",at(side,(left+right)/2,(sill+top)/2),(.045,right-left-.09,top-sill),rot)
            m.box("rubber",at(side,left+.035,(sill+top)/2),(.075,.07,top-sill),rot)
        m.box("rubber",at(side,b-.035,(sill+top)/2),(.075,.07,top-sill),rot)
        if stripe:
            m.box(stripe,at(side*1.01,mid,sill-.18),(.045,b-a,.24),rot)


def bus_shell(m, x, y, rot=0.0, color="bus_green", z=0.0, length=11.0):
    at = placer(x, y, z, rot)
    k = length / 11.0
    # Floor, roof and thin outer panels leave a real saloon that can be entered from the kerb.
    m.box(color, at(0, 0, .40), (2.5, length, .16), rot)
    vehicle_roof(m, at, "white", 2.5, length, 2.78, .16)
    for end in (-1, 1):
        m.box(color, at(0, end * (length / 2 - .08), 1.55), (2.5, .16, 2.25), rot)
    for sx in (-1, 1):
        vehicle_side(m,at,sx*1.24,length,[4.55*k,-1.0*k] if sx>0 else [],.58*k,color,.4,1.42,2.56,2.78,rot)
    for i in range(6):
        yy=(-4.1+i*1.55)*k
        m.box("seat_blue", at(-.76, yy, .73), (.72, .67*k, .13), rot)
        m.box("seat_blue", at(-.76, yy-.25*k, 1.03), (.72, .10*k, .60), rot)
    for yy in (2.9,2.05,-2.4,-3.25,-4.1):
        m.box("seat_blue",at(.78,yy*k,.73),(.66,.63*k,.13),rot)
        m.box("seat_blue",at(.78,(yy-.25)*k,1.03),(.66,.1*k,.60),rot)
    for yy in (-3.5*k, .4*k, 3.6*k):
        m.cylinder("chrome", at(.32, yy, .5), .035, 2.15, 10)
    m.box("metal", at(0, -2.5 * k, 3.1), (1.6, 2.8 * k, .3), rot)
    # Seoul's low-floor city buses have a deep front windscreen, high LED destination
    # board, low entry doors and two axles. Leave the actual kerb-side doorways open.
    m.box("glass", at(0, length / 2 + .015, 2.02), (2.22, .045, 1.40), rot, rot_x=-.10)
    m.box("glass", at(0, -length / 2 - .01, 2.05), (2.0, .05, .9), rot)
    for sx in (-1.26, 1.26):
        for sy in (-3.4, 3.6):
            m.lathe("rubber",[(.40,-.16),(.47,-.13),(.5,-.08),(.5,.08),(.47,.13),(.40,.16)],at(sx*.92,sy*k,.5),24,axis="X",rot_z=rot)
            m.cylinder("chrome",at(sx*1.06,sy*k,.5),.25,.055,20,axis="X",rot_z=rot)
    m.box("darkmetal", at(0, length / 2 + .05, 2.62), (1.86, .045, .34), rot)
    m.box("screen", at(0, length / 2 + .08, 2.62), (1.55, .045, .20), rot)
    for sx in (-1,1):
        m.box("darkmetal", at(sx*.67,length/2+.055,1.44),(.83,.035,.027),rot,rot_x=sx*.12)
        m.box("darkmetal", at(sx*1.26,length/2-.38,2.15),(.16,.65,.08),rot)
        m.box("darkmetal", at(sx*1.39,length/2-.03,2.12),(.32,.12,.27),rot)
        m.box("rubber", at(sx*.9,-length/2-.045,.84),(.24,.04,.16),rot)
        m.box("red", at(sx*.9,-length/2-.07,1.12),(.36,.045,.16),rot)
    for sx in (-.9, .9):
        m.box("light", at(sx, length / 2 + .08, .84), (.47, .05, .18), rot)
        m.box("orange", at(sx*1.16,length/2+.08,.84),(.16,.05,.16),rot)


def metro_shell(m, x, y, rot=0.0, z=0.0, length=19.5, stripe="metro_stripe"):
    at = placer(x, y, z, rot)
    m.box("darkmetal", at(0, 0, .91), (3.1, length, .16), rot)
    vehicle_roof(m, at, "metal", 3.1, length, 3.35, .17)
    for end in (-1, 1):
        m.box("metro_body", at(0, end*(length/2-.08), 2.15), (3.1, .16, 2.5), rot)
    k=length/19.5
    for sx in (-1, 1):
        vehicle_side(m,at,sx*1.54,length,[d*k for d in [-7.55,-2.52,2.52,7.55]],.67*k,"metro_body",.91,1.85,2.99,3.35,rot,stripe)
        for yy in (-5.035*k, 0, 5.035*k):
            m.box("seat_red", at(sx*1.13, yy, 1.35), (.6, 3.6*k, .12), rot)
            m.box("seat_red", at(sx*1.42, yy, 1.69), (.12, 3.6*k, .68), rot)
        for yy in (-min(6.0, length*.30), 0, min(6.0, length*.30)):
            m.cylinder("chrome", at(sx*.8, yy, 1.0), .04, 2.2, 10)
    m.box("glass", at(0, length / 2 + .01, 2.4), (2.4, .05, 1.2), rot)
    m.box("darkmetal",at(0,length/2+.05,3.12),(1.45,.05,.23),rot)
    m.box("screen",at(0,length/2+.08,3.12),(1.27,.05,.13),rot)
    for sx in (-1,1):
        m.box("light",at(sx*1.13,length/2+.05,1.35),(.25,.06,.17),rot)
        m.box("red",at(sx*1.13,-length/2-.05,1.35),(.25,.06,.17),rot)
    for sx in (-1.56, 1.56):
        for sy in (-length / 2 + 2.5, length / 2 - 2.5):
            for dd in (-.9, .9):
                m.cylinder("rubber", at(sx * .7, sy + dd, .45), .42, .2, 14, axis="X", rot_z=rot)


def rails(m, x0, x1, y, z=0.06):
    m.block("stone", x0, y - .9, z, x1, y + .9, z + .12)
    for k in range(int((x1 - x0) / .7)):
        m.box("darkwood", (x0 + .35 + k * .7, y, z + .16), (.22, 1.9, .08))
    for dy in (-.72, .72):
        m.box("metal", ((x0 + x1) / 2, y + dy, z + .26), (x1 - x0, .08, .12))


# ---------- buildings ----------

def build_camp():
    m = Model("camp")
    lot(m, "soil", walk=False)
    for x, y, s in ((-2.4, .8, 1.0), (2.2, 1.4, .9), (-.2, 2.6, .8)):
        m.cone("fabric_yellow" if s < 1 else "thatch", (x, y, .06), 1.5 * s, 2.4 * s, 8)
        m.cylinder("timber", (x, y, 2.2 * s), .05, .7 * s, 6)
    m.cylinder("stone", (0, -.8, .06), .6, .18, 10)
    m.cone("fire", (0, -.8, .24), .35, .6, 8)
    for a in range(4):
        m.box("timber", (math.cos(a * 1.6) * 1.4, -.8 + math.sin(a * 1.6) * 1.4, .25), (1.2, .3, .3), a * 1.6)
    fence(m, -4.4, 4.4, 4.2, .8)
    pine(m, 3.8, -3.2, .9)
    pine(m, -3.9, -3.0, 1.1)
    return m.finish()


def build_pit_house():
    m = Model("pit-house")
    lot(m, "soil", walk=False)
    for x, y, s in ((-1.6, .6, 1.0), (2.2, -.4, .8)):
        m.cylinder("soil", (x, y, .0), 2.2 * s, .35, 14)
        m.lathe("thatch", [(2.3 * s, .3), (1.9 * s, 1.1 * s), (1.0 * s, 2.4 * s), (.15 * s, 3.2 * s), (.001, 3.25 * s)],
                (x, y, 0), 14)
        m.box("thatch", (x, y - 2.0 * s, .7), (.9 * s, 1.2 * s, 1.0 * s), rot_x=-.4)
        m.box("darkwood", (x, y - 2.45 * s, .55), (.6 * s, .05, .8 * s), rot_x=-.4)
    for i in range(5):
        m.lathe("pottery", [(.18, 0), (.28, .25), (.2, .55), (.12, .62)], (-3.6 + i * .5, -3.2, .06), 10)
    m.cylinder("stone", (.6, -2.6, .06), .45, .15, 10)
    m.cone("fire", (.6, -2.6, .21), .25, .45, 8)
    pine(m, 3.7, 3.4, 1.0)
    tree(m, -3.8, 3.5, .9, dark=True)
    return m.finish()


def build_farm():
    m = Model("farm")
    lot(m, "soil", walk=False)
    for row in range(9):
        y = -3.9 + row * .62
        m.block("crop" if row % 3 else "wheat", -4.5, y, .06, 1.2, y + .38, .36)
    m.block("plaster", 1.8, -.6, .06, 4.4, 2.6, 2.2)
    m.curved_roof("thatch", 3.1, 1.0, 2.2, 3.4, 4.0, 1.4, .1)
    m.box("darkwood", (3.1, -.62, 1.0), (1.0, .05, 1.6))
    for x in (2.0, 3.0):
        m.lathe("wheat", [(.55, 0), (.55, .7), (.3, 1.2), (.01, 1.35)], (x, -2.7, .06), 10)
    fence_y(m, -4.3, 4.3, 1.5, .8)
    tree(m, 4.1, 3.8, .8)
    return m.finish()


def build_forge():
    m = Model("forge")
    lot(m, "soil")
    m.block("stone", -3.0, -1.5, .06, 1.5, 2.8, 2.4)
    m.prism("thatch", -3.0, 1.5, -1.5, 2.8, 2.4, 1.6, .4)
    m.block("darkwood", -1.2, -1.53, .06, .0, -1.48, 1.8)
    chimney(m, .9, 2.2, 2.4, 2.4, .35, stripes=False)
    m.block("stone", 2.0, -2.0, .06, 3.6, -.6, 1.0)
    m.box("fire", (2.8, -2.02, .6), (.6, .04, .4))
    m.box("darkmetal", (2.8, -3.0, .5), (.7, .3, .25))
    m.box("darkmetal", (2.8, -3.0, .25), (.25, .25, .4))
    for i in range(4):
        m.lathe("darkmetal", [(.12, 0), (.12, .6)], (-3.8 + i * .35, -3.2, .06), 8)
    for i in range(3):
        m.box("timber", (3.5, 1.0 + i * .35, .25 + (i % 2) * .3), (1.6, .3, .3))
    tree(m, 3.8, 3.6, .8)
    return m.finish()


def build_market():
    m = Model("market")
    lot(m, "pavement", walk=False)
    colors = ("fabric_red", "fabric_blue", "fabric_yellow", "green")
    for i, (x, y) in enumerate(((-3.0, -1.8), (0, -1.8), (3.0, -1.8), (-3.0, 1.8), (0, 1.8), (3.0, 1.8))):
        m.block("timber", x - 1.1, y - .7, .06, x + 1.1, y + .7, .9)
        for dx in (-1.1, 1.1):
            for dy in (-.8, .8):
                m.box("timber", (x + dx, y + dy, 1.2), (.1, .1, 2.3))
        m.prism(colors[i % 4], x - 1.2, x + 1.2, y - .9, y + .9, 2.3, .6, .15)
        for k in range(3):
            m.box(("crop", "orange", "red", "wheat")[(i + k) % 4], (x - .7 + k * .7, y - .3, 1.0), (.5, .4, .2))
    for x in (-4.3, 4.3):
        m.box("timber", (x, 0, .3), (.6, .6, .5))
    tree(m, 0, 4.0, .7)
    return m.finish()


def build_workshop():
    m = Model("workshop")
    lot(m, "soil")
    m.block("granite", -3.8, -1.2, .06, 2.2, 3.2, .45)
    m.block("plaster", -3.5, -.9, .45, 1.9, 2.9, 2.6)
    for x in (-3.5, -1.7, .1, 1.9):
        m.box("timber", (x, -.9, 1.5), (.2, .2, 2.1))
    m.curved_roof("giwa", -.8, 1.0, 2.6, 6.4, 5.2, 1.5, .4)
    windows_x(m, -.93, -3.3, 1.7, .8, 2.3, 1, 3, "darkwood")
    for i in range(5):
        m.box("timber", (3.4, -2.5 + i * .5, .2 + (i % 2) * .15), (2.0, .35, .35))
    m.box("timber", (3.4, 2.0, .5), (1.2, 1.0, .9))
    tree(m, 3.8, 3.8, .8)
    return m.finish()


def build_kiln():
    m = Model("kiln")
    lot(m, "soil")
    for i in range(6):
        y = -2.8 + i * 1.15
        z = .06 + i * .32
        m.lathe("brick", [(1.1, 0), (1.1, .4), (.85, 1.05), (.4, 1.4), (.01, 1.45)], (-1.0, y, z), 14)
    chimney(m, -1.0, 4.1, 2.0, 1.6, .3, stripes=False)
    m.box("fire", (-1.0, -3.92, .6), (.6, .04, .5))
    for i in range(9):
        key = "celadon" if i % 3 == 0 else "pottery"
        m.lathe(key, [(.18, 0), (.3, .3), (.22, .65), (.12, .72)], (2.0 + (i % 3) * .75, -2.6 + (i // 3) * .8, .06), 10)
    m.block("timber", 1.6, 1.0, .06, 4.2, 3.6, 1.6)
    m.prism("thatch", 1.6, 4.2, 1.0, 3.6, 1.6, .8, .2)
    return m.finish()


def hanok_wing(m, x0, x1, y0, y1, along_x=True):
    m.block("granite", x0 - .3, y0 - .3, .06, x1 + .3, y1 + .3, .55)
    m.block("plaster", x0, y0, .55, x1, y1, 2.4)
    if along_x:
        n = int((x1 - x0) / 1.2)
        for i in range(n + 1):
            x = x0 + (x1 - x0) * i / n
            for y in (y0, y1):
                m.box("timber", (x, y, 1.5), (.18, .18, 1.95))
        for i in range(n):
            x = x0 + (x1 - x0) * (i + .5) / n
            m.box("darkwood", (x, y0 - .02, 1.45), ((x1 - x0) / n * .7, .05, 1.3))
            for k in (-.25, 0, .25):
                m.box("timber", (x + k * (x1 - x0) / n, y0 - .05, 1.45), (.04, .04, 1.3))
            m.box("timber", (x, y0 - .05, 1.45), ((x1 - x0) / n * .7, .04, .04))
        m.curved_roof("giwa", (x0 + x1) / 2, (y0 + y1) / 2, 2.4, x1 - x0 + 1.6, y1 - y0 + 1.9, 1.7, .55)
    else:
        n = int((y1 - y0) / 1.2)
        for i in range(n + 1):
            y = y0 + (y1 - y0) * i / n
            for x in (x0, x1):
                m.box("timber", (x, y, 1.5), (.18, .18, 1.95))
        m.curved_roof("giwa", (x0 + x1) / 2, (y0 + y1) / 2, 2.4, y1 - y0 + 1.6, x1 - x0 + 1.9, 1.7, .55, rot_z=math.pi / 2)


def build_hanok():
    m = Model("hanok")
    lot(m, "soil")
    hanok_wing(m, -3.6, 2.4, 1.2, 3.4)
    hanok_wing(m, 1.9, 3.9, -2.0, 1.2, along_x=False)
    for x0, x1, y in ((-4.6, 4.6, 4.3), (-4.6, -1.0, -3.7), (1.0, 4.6, -3.7)):
        m.block("granite", x0, y - .2, .06, x1, y + .2, 1.2)
        m.prism("giwa", x0, x1, y - .2, y + .2, 1.2, .3, .15)
    for x in (-4.6, 4.6):
        m.block("granite", x - .2, -3.7, .06, x + .2, 4.3, 1.2)
        m.prism("giwa", x - .2, x + .2, -3.7, 4.3, 1.2, .3, .15)
    m.block("timber", -1.0, -3.8, .06, 1.0, -3.6, 2.0)
    m.curved_roof("giwa", 0, -3.7, 2.0, 2.6, 1.3, .6, .2)
    m.block("darkwood", -.6, -3.83, .06, .6, -3.78, 1.8)
    m.lathe("pottery", [(.25, 0), (.4, .4), (.3, .8), (.18, .9)], (-3.6, -2.4, .06), 10)
    m.lathe("pottery", [(.2, 0), (.32, .3), (.24, .65)], (-3.0, -2.6, .06), 10)
    tree(m, -2.0, -1.4, .9)
    return m.finish()


def build_school():
    m = Model("school")
    lot(m, "lawn")
    m.block("granite", -4.2, -.2, .06, 4.2, 3.6, .7)
    m.block("plaster", -3.8, .2, .7, 3.8, 3.2, 3.0)
    for i in range(8):
        x = -3.8 + i * 7.6 / 7
        m.box("timber", (x, .2, 1.85), (.22, .22, 2.3))
    windows_x(m, .17, -3.6, 3.6, 1.0, 2.8, 1, 6, "darkwood")
    m.curved_roof("giwa", 0, 1.7, 3.0, 9.2, 4.6, 2.0, .6)
    m.box("granite", (0, -.6, .2), (2.2, .8, .3))
    m.block("pavement", -.8, -3.9, .06, .8, -.4, .1)
    for x in (-3.6, 3.6):
        tree(m, x, -2.4, 1.0)
    m.box("sign", (0, .14, 2.6), (1.4, .06, .4))
    return m.finish()


def build_railworks():
    m = Model("railworks")
    lot(m, "concrete")
    m.block("brick", -4.4, -1.0, .06, 4.4, 3.9, 4.2)
    for i in range(4):
        x0 = -4.4 + i * 2.2
        m.prism("roof_grey", x0, x0 + 2.2, -1.0, 3.9, 4.2, 1.2, .05)
        m.box("glass", (x0 + 1.1, -1.04, 3.3), (1.6, .05, 1.0))
    for x in (-2.2, 2.2):
        m.block("darkmetal", x - 1.0, -1.04, .1, x + 1.0, -1.0, 3.0)
    rails(m, -4.8, 4.8, -2.4)
    m.box("darkmetal", (1.0, -2.4, 1.3), (5.0, 2.2, 2.0))
    m.cylinder("darkmetal", (-1.0, -2.4, 2.3), .5, 1.2, 12)
    m.box("red", (1.0, -2.4, .5), (5.2, 2.3, .25))
    m.box("darkmetal", (3.0, -2.4, 2.6), (1.6, 2.2, 1.1))
    chimney(m, 4.0, 3.4, 4.2, 4.0, .3, stripes=False)
    return m.finish()


def build_house():
    m = Model("house")
    lot(m, "lawn")
    m.block("white", -3.2, -.8, .06, 2.4, 3.6, 3.0)
    m.block("plaster", -3.2, -.8, 3.0, .8, 3.6, 5.6)
    m.prism("roof_grey", -3.2, .8, -.8, 3.6, 5.6, 1.8, .45)
    m.prism("roof_grey", .8, 2.4, -.8, 3.6, 3.0, .7, .3)
    windows_x(m, -.83, -3.0, .6, 3.1, 5.4, 1, 2)
    windows_x(m, -.83, -1.2, 2.2, .4, 2.8, 1, 2)
    m.block("darkwood", -2.6, -.86, .06, -1.6, -.8, 2.2)
    m.block("concrete", -3.3, -1.6, 3.0, .9, -.8, 3.12)
    fence(m, -3.4, .9, -1.6, .55, "white", .35)
    m.block("concrete", 2.6, -3.9, .06, 4.6, 1.8, .1)
    car(m, 3.6, -1.0, 0, "car_white", .1)
    fence(m, -4.7, 2.5, -3.95, .9, "white", .45)
    fence_y(m, -3.95, 4.4, -4.7, .9, "white", .45)
    tree(m, -3.9, -2.8, .9)
    tree(m, 3.9, 3.6, 1.0, dark=True)
    m.block("water", -1.2, -3.3, .06, .8, -2.2, .1)
    return m.finish()


def build_coal_power():
    m = Model("coal-power")
    lot(m, "concrete")
    m.block("factory_blue", -4.4, .4, .06, .6, 4.2, 5.5)
    m.block("darkmetal", -4.4, .4, 5.5, .6, 4.2, 5.8)
    windows_x(m, .37, -4.2, .4, 1.0, 5.0, 3, 5)
    m.block("rust", .6, 1.0, .06, 3.2, 4.0, 7.5)
    chimney(m, 3.7, 3.6, .06, 16.0, .6)
    m.lathe("coal", [(2.0, 0), (1.4, .9), (.3, 1.6), (.01, 1.65)], (-2.4, -2.2, .06), 16)
    m.box("darkmetal", (.0, -1.0, 3.0), (5.0, .6, .5), rot_y=-.55)
    for x, h in ((-.7, 2.0), (1.6, 4.0)):
        m.box("metal", (x, -1.0, h / 2), (.2, .2, h))
    pylon(m, 3.8, -2.6, 6.0)
    return m.finish()


def build_power():
    m = Model("power")
    lot(m, "concrete")
    m.block("white", -4.4, .8, .06, 1.6, 4.2, 4.2)
    m.block("steel_blue", -4.4, .8, 4.2, 1.6, 4.2, 4.6)
    windows_x(m, .77, -4.2, 1.4, 1.0, 3.8, 2, 6)
    chimney(m, -3.6, 3.4, 4.6, 6.0, .4, stripes=False)
    chimney(m, -2.4, 3.4, 4.6, 6.0, .4, stripes=False)
    m.block("pavement", -4.6, -3.9, .06, 4.6, .4, .1)
    for i in range(3):
        x = -3.4 + i * 1.6
        m.block("darkmetal", x - .5, -2.4, .1, x + .5, -1.6, 1.4)
        for k in range(4):
            m.box("metal", (x - .35 + k * .23, -2.0, 1.6), (.08, .08, .5))
    fence(m, -4.6, 4.6, -3.8, 1.4, "metal", .6)
    pylon(m, 3.0, -1.5, 7.0)
    pylon(m, 3.0, 2.5, 7.0)
    ac_units(m, (-1.0, .4), 2.5, 4.6)
    return m.finish()


def build_oil_power():
    m = Model("oil-power")
    lot(m, "concrete")
    for x, y in ((-2.8, -1.8), (-2.8, 1.8), (.4, 1.8)):
        m.lathe("white", [(1.5, 0), (1.5, 2.6), (1.3, 2.9), (.01, 3.0)], (x, y, .06), 20)
        m.lathe("red", [(1.52, 2.0), (1.52, 2.3)], (x, y, .06), 20, cap=False)
        m.box("metal", (x + 1.5, y, 1.4), (.06, .3, 2.8))
    m.block("concrete", 1.8, -3.0, .06, 4.6, 1.0, 4.0)
    windows_x(m, -3.03, 2.0, 4.4, 1.0, 3.6, 2, 3)
    chimney(m, 3.8, 2.8, .06, 12.0, .45)
    for y in (-3.6, -3.2):
        m.cylinder("darkmetal", (-.5, y, .5), .12, 8.0, 8, axis="X")
    pylon(m, .6, -2.0, 5.5)
    return m.finish()


def build_factory():
    m = Model("factory")
    lot(m, "concrete")
    m.block("factory_blue", -4.4, -.6, .06, 3.2, 4.2, 4.0)
    for i in range(4):
        x0 = -4.4 + i * 1.9
        m.prism("metal", x0, x0 + 1.9, -.6, 4.2, 4.0, .9, .02)
    m.block("white", 3.2, -.6, .06, 4.6, 2.0, 5.5)
    windows_y(m, 4.62, -.4, 1.8, 1.0, 5.2, 3, 2)
    for x in (-3.4, -1.8, -.2, 1.4):
        m.block("darkmetal", x - .6, -.64, .06, x + .6, -.6, 2.4)
    m.box("sign", (2.6, -.64, 3.4), (1.2, .05, .5))
    car(m, -3.0, -2.6, math.pi / 2, "car_silver", .06)
    m.box("white", (1.0, -2.4, 1.3), (4.6, 2.3, 2.3))
    m.box("orange", (3.6, -2.4, 1.0), (1.2, 2.2, 1.9))
    m.box("glass", (4.21, -2.4, 1.4), (.05, 1.8, .7))
    for x in (-.4, 2.4, 3.8):
        for y in (-3.5, -1.3):
            m.cylinder("rubber", (x, y, .45), .42, .3, 12, axis="Y")
    ac_units(m, (-3.4, -1.0, 1.4), 2.6, 4.9)
    return m.finish()


def build_apartment():
    m = Model("apartment")
    lot(m, "lawn")
    floors, fh = 12, 2.6
    top = .3 + floors * fh
    m.block("white", -4.2, -.2, .3, 4.2, 3.4, top)
    m.block("concrete", -4.4, -.4, .06, 4.4, 3.6, .3)
    for f in range(floors):
        z = .3 + f * fh
        m.block("concrete", -4.0, -1.1, z + .1, 4.0, -.2, z + .25)
        m.block("glass", -3.8, -.25, z + .5, 3.8, -.2, z + 2.2)
        m.block("white", -4.0, -1.12, z + .25, 4.0, -1.08, z + 1.2)
        for x in (-1.35, 1.35):
            m.block("white", x - .1, -1.1, z + .25, x + .1, -.2, z + fh)
        for k in range(6):
            if (f * 7 + k * 3) % 5 == 0:
                m.box("glass_lit", (-3.4 + k * 1.36, -.19, z + 1.3), (.9, .02, 1.2))
        windows_x(m, 3.42, -3.8, 3.8, z, z + fh, 1, 6)
        windows_y(m, 4.22, .2, 3.0, z, z + fh, 1, 2)
        windows_y(m, -4.22, .2, 3.0, z, z + fh, 1, 2)
    m.block("green", -4.22, -.2, top - 3.5, -3.9, 3.4, top)
    m.block("concrete", -1.5, .5, top, 1.5, 2.8, top + 2.4)
    m.box("sign", (0, .48, top + 1.5), (2.4, .05, .6))
    m.block("darkmetal", -4.2, -.2, top, 4.2, 3.4, top + .3)
    m.block("glass", -.8, -.24, .3, .8, -.2, 2.6)
    m.block("concrete", -1.2, -1.6, 2.6, 1.2, -.2, 2.8)
    for x in (-3.8, 3.8):
        tree(m, x, -2.8, .9)
    m.block("pavement", -.8, -3.9, .06, .8, -1.6, .1)
    car(m, 2.4, -2.7, math.pi / 2, "car_black", .06)
    return m.finish()


def build_nuclear():
    m = Model("nuclear")
    lot(m, "concrete")
    for x in (-2.8, .2):
        m.cylinder("dome", (x - .3, 1.8, .06), 1.4, 3.6, 24)
        m.lathe("dome", [(1.4, 0), (1.25, .6), (.9, 1.05), (.45, 1.3), (.01, 1.38)], (x - .3, 1.8, 3.66), 24)
    m.block("white", -4.4, -1.0, .06, 1.4, .4, 3.2)
    windows_x(m, -1.03, -4.2, 1.2, .8, 3.0, 2, 6)
    profile = [(2.4, 0), (2.0, 3.0), (1.6, 6.5), (1.55, 8.0), (1.75, 10.0)]
    m.lathe("cooling", profile, (2.3, 1.6, .06), 28, cap=False)
    m.lathe("cooling", [(r - .12, h) for r, h in reversed(profile)], (2.3, 1.6, .06), 28, cap=False)
    m.lathe("concrete", [(1.62, 10.0), (1.75, 10.0)], (2.3, 1.6, .06), 28, cap=False)
    fence(m, -4.6, 4.6, -3.8, 1.6, "metal", .7)
    m.box("pavement", (0, -2.6, .08), (9.2, 1.6, .05))
    pylon(m, -3.3, -2.6, 6.0)
    return m.finish()


def build_wind():
    m = Model("wind")
    lot(m, "grass", walk=False)
    m.cylinder("concrete", (0, 0, .06), 1.2, .3, 16)
    m.lathe("white", [(.42, 0), (.22, 20.0)], (0, 0, .36), 16)
    m.box("white", (0, .4, 20.6), (.9, 2.6, .9))
    m.sphere("white", (0, -1.0, 20.6), .5, 2)
    for k in range(3):
        a = k * math.tau / 3 + .3
        for seg in range(4):
            r = .9 + seg * 1.0
            w = .55 - seg * .1
            m.box("white", (math.sin(a) * r, -1.15, 20.6 + math.cos(a) * r), (w, .12, 1.1), rot_y=a)
    m.block("concrete", 1.6, -1.0, .06, 3.2, .4, 2.4)
    m.box("darkmetal", (2.4, -1.03, 1.0), (.7, .05, 1.6))
    for x, y in ((-3.5, -3.0), (3.6, 3.2), (-3.4, 3.4)):
        m.lathe("crop", [(.6, 0), (.4, .4), (.01, .45)], (x, y, .06), 8)
    return m.finish()


def build_bus_depot():
    m = Model("bus-depot")
    lot(m, "asphalt", walk=False)
    for x in range(-4, 5, 2):
        m.box("line_white", (x, .4, .08), (.08, 9.0, .02))
    for x in (-4.4, 4.4):
        for y in (-2.8, 3.6):
            m.box("metal", (x, y, 2.4), (.2, .2, 4.8))
    m.block("steel_blue", -4.6, -3.2, 4.8, 4.6, 4.0, 5.0)
    for k in range(4):
        m.block("solar", -4.4 + k * 2.25, -3.0, 5.0, -2.3 + k * 2.25, 3.8, 5.06)
    bus_shell(m, -2.2, .4, 0, "bus_green", .06, 8.4)
    bus_shell(m, 2.2, .4, 0, "bus_blue", .06, 8.4)
    m.block("white", -4.8, -4.5, .06, -2.0, -3.4, 2.8)
    windows_x(m, -4.53, -4.6, -2.2, .6, 2.6, 1, 2)
    m.box("sign", (-3.4, -4.53, 2.4), (1.6, .05, .35))
    return m.finish()


def build_metro_depot():
    m = Model("metro-depot")
    lot(m, "concrete", walk=False)
    for y in (-2.0, 2.0):
        rails(m, -4.8, 4.8, y)
    for x in (-3.6, 4.6):
        m.block("brick", x - .2, -4.2, .06, x + .2, 4.2, 5.4)
    m.block("roof_grey", -3.8, -4.4, 5.4, 4.8, 4.4, 5.7)
    for k in range(4):
        m.block("glass", -3.2 + k * 2.0, -2.8, 5.7, -2.0 + k * 2.0, 2.8, 5.95)
    for y, stripe in ((-2.0, "metro_stripe"), (2.0, "orange")):
        metro_shell(m, .0, y, math.pi / 2, .3, length=9.4, stripe=stripe)
    m.block("white", -4.8, -4.5, .06, -4.0, 4.5, 3.2)
    windows_y(m, -4.82, -4.0, 4.0, .4, 3.0, 2, 5)
    return m.finish()


# ---------- vehicles, cabins, street props ----------

def build_bus():
    m = Model("Bus")
    bus_shell(m, 0, 0, 0, "bus_green")
    return m.finish()


def build_metro():
    m = Model("Metro")
    metro_shell(m, 0, 0)
    return m.finish()


def build_car(name="Car", body="car_silver"):
    m = Model(name)
    car(m, 0, 0, 0, body)
    return m.finish()


CAR_VARIANTS = [("CarRed", "car_red"), ("CarWhite", "car_white"), ("CarBlack", "car_black"), ("CarBlue", "sign")]


def cabin_common(m, half_w, half_l, height):
    m.block("darkmetal", -half_w, -half_l, 0, half_w, half_l, .1)
    m.block("pavement", -half_w + .05, -half_l, .1, half_w - .05, half_l, .12)
    m.block("white", -half_w, -half_l, height, half_w, half_l, height + .08)
    for x in (-.5, .5):
        m.block("light", x - .12, -half_l + .3, height - .05, x + .12, half_l - .3, height)
    for sx in (-1, 1):
        x = sx * half_w
        m.block("white", x - .05, -half_l, .1, x + .05, half_l, 1.0)
        m.block("white", x - .05, -half_l, 2.0, x + .05, half_l, height)
        n = int(half_l * 2 / 1.5)
        for i in range(n + 1):
            m.box("white", (x, -half_l + i * half_l * 2 / n, 1.5), (.1, .12, 1.0))
        m.box("chrome", (sx * (half_w - .55), 0, height - .35), (.04, half_l * 2 - .4, .04))
        for i in range(int(half_l * 2 / .8)):
            y = -half_l + .4 + i * .8
            m.box("chrome", (sx * (half_w - .55), y, height - .55), (.03, .03, .4))
            m.lathe("yellow", [(.07, 0), (.07, .04)], (sx * (half_w - .55), y, height - .78), 10)
    for i in range(int(half_l * 2 / 3.0) + 1):
        y = -half_l + 1.5 + i * 3.0
        if abs(y) < half_l:
            m.cylinder("chrome", ((-1) ** i * (half_w - .95), y, .12), .04, height - .12, 8)
    for end in (-1, 1):
        for x in (-half_w + .1, half_w - .1):
            m.box("white", (x, end * half_l, height / 2 + .05), (.16, .16, height - .1))
        m.box("white", (0, end * half_l, height - .12), (half_w * 2, .16, .24))


def build_bus_cabin():
    m = Model("BusCabin")
    half_w, half_l, h = 1.15, 5.2, 2.35
    cabin_common(m, half_w, half_l, h)
    for sx in (-1, 1):
        for i in range(6):
            y = -4.4 + i * 1.3
            if sx > 0 and -.6 < y < 1.2:
                continue
            x = sx * (half_w - .45)
            m.box("seat_blue", (x, y, .55), (.75, .5, .12))
            m.box("seat_blue", (x, y - .26, .95), (.75, .08, .7), rot_x=-.12)
            m.box("darkmetal", (x, y, .3), (.1, .1, .4))
    m.block("darkmetal", -half_w, half_l - .3, .1, half_w, half_l, 1.1)
    m.box("darkmetal", (-.5, half_l - .7, 1.0), (.4, .05, .4), rot_x=.6)
    m.box("sign", (0, half_l - .2, h - .25), (1.2, .05, .25))
    return m.finish()


def build_metro_cabin():
    m = Model("MetroCabin")
    half_w, half_l, h = 1.45, 9.5, 2.3
    cabin_common(m, half_w, half_l, h)
    for sx in (-1, 1):
        for k in range(3):
            y = -6.0 + k * 6.0
            m.block("seat_red", sx * (half_w - .5) - .25, y - 1.9, .4, sx * (half_w - .5) + .25, y + 1.9, .52)
            m.block("seat_red", sx * (half_w - .12) - .06, y - 1.9, .52, sx * (half_w - .12) + .06, y + 1.9, 1.05)
            m.box("chrome", (sx * (half_w - .3), y, 1.95), (.35, 3.8, .04))
        for y in (-8.9, -3.0, 3.0, 8.9):
            m.box("darkmetal", (sx * half_w, y, 1.1), (.08, 1.3, 2.0))
    m.box("sign", (0, -half_l + .1, h - .3), (1.6, .05, .3))
    m.box("sign", (0, half_l - .1, h - .3), (1.6, .05, .3))
    return m.finish()


def build_tree():
    m = Model("Tree")
    tree(m, 0, 0, 1.0)
    return m.finish()


def build_pine():
    m = Model("Pine")
    pine(m, 0, 0, 1.2)
    return m.finish()


def build_street_light():
    m = Model("StreetLight")
    m.cylinder("darkmetal", (0, 0, 0), .08, 5.0, 8)
    m.box("darkmetal", (0, .7, 4.95), (.1, 1.5, .1))
    m.box("light", (0, 1.4, 4.85), (.3, .5, .1))
    return m.finish()


def build_traffic_light():
    """Pole with an arm over the road toward +Y; Unity adds the coloured lamp at the arm end."""
    m = Model("TrafficLight")
    m.cylinder("darkmetal", (0, 0, 0), .1, 5.0, 10)
    m.box("darkmetal", (0, 1.4, 4.85), (.12, 2.8, .12))
    m.box("darkmetal", (0, 2.5, 4.45), (.4, .35, 1.0))
    for z in (4.1, 4.45, 4.8):
        m.cylinder("rubber", (0, 2.33, z), .12, .04, 12, axis="Y")
    m.box("darkmetal", (.25, 0, 1.3), (.3, .25, .45))
    return m.finish()


def build_road():
    """14 m straight along X, 3.2 m wide."""
    m = Model("Road")
    m.block("asphalt", -7.0, -1.6, 0, 7.0, 1.6, .05)
    for dy in (-.07, .07):
        m.block("line_yellow", -7.0, dy - .04, .05, 7.0, dy + .04, .056)
    for dy in (-1.45, 1.45):
        m.block("line_white", -7.0, dy - .04, .05, 7.0, dy + .04, .056)
    return m.finish()


# ---------- people and doors: separate parts so Unity can animate them ----------
# Person faces +Y like the vehicles. Each part's origin is its joint: hips for the body and legs,
# shoulders for the arms. Unity assembles them (hips 0.9 m, shoulders 1.45 m above the ground).

def build_person_body():
    m = Model("PersonBody")
    m.sphere("pants", (0, 0, .06), .22, 1, squash=.7)
    m.sphere("shirt", (0, 0, .33), .26, 1, squash=1.6)
    m.box("shirt", (0, 0, .52), (.44, .24, .12))
    m.cylinder("skin", (0, 0, .58), .06, .08, 8)
    m.sphere("skin", (0, .01, .76), .12, 2)
    m.sphere("hair", (0, -.02, .80), .125, 2, squash=.8)
    return m.finish()


def build_person_leg():
    m = Model("PersonLeg")
    m.sphere("pants", (0, 0, -.42), .115, 1, squash=3.6)
    m.box("shoe", (0, .04, -.86), (.13, .26, .08))
    return m.finish()


def build_person_arm():
    m = Model("PersonArm")
    m.sphere("shirt", (0, 0, -.16), .115, 1, squash=1.5)
    m.sphere("skin", (0, 0, -.45), .09, 1, squash=1.5)
    return m.finish()


def build_door_frame():
    """Door frame in the XZ plane, front toward -Y like building facades; opening 0.9 x 2.0 m."""
    m = Model("DoorFrame")
    for x in (-.5, .5):
        m.box("darkwood", (x, 0, 1.05), (.1, .14, 2.1))
    m.box("darkwood", (0, 0, 2.15), (1.1, .14, .12))
    m.box("concrete", (0, -.2, .03), (1.2, .4, .06))
    return m.finish()


def build_door_leaf():
    """Leaf hinged on its own origin edge (x = 0), 0.9 m wide toward +X."""
    m = Model("DoorLeaf")
    m.box("door_wood", (.45, 0, 1.0), (.9, .05, 2.0))
    for z in (.55, 1.45):
        m.box("timber", (.45, -.03, z), (.7, .02, .6))
    m.sphere("brass", (.8, -.07, 1.0), .04, 1)
    return m.finish()


def build_bus_red():
    m = Model("BusRed")
    bus_shell(m, 0, 0, 0, "red")
    return m.finish()


def build_bus_yellow():
    m = Model("BusYellow")
    bus_shell(m, 0, 0, 0, "yellow")
    return m.finish()


def build_bus_blue():
    """Seoul trunk-line (blue) bus that runs in the median bus lanes."""
    m = Model("BusBlue")
    bus_shell(m, 0, 0, 0, "bus_blue")
    return m.finish()


def ktx_car(m, y0, y1, nose_front=False, nose_back=False):
    """One KTX car between y0 and y1 (Blender Y forward); a streamlined nose replaces the cab end."""
    nose = 6.5
    body0 = y0 + (nose if nose_back else 0)
    body1 = y1 - (nose if nose_front else 0)
    mid = (body0 + body1) / 2
    length = body1 - body0
    m.box("ktx_white", (0, mid, .91), (2.95, length, .16))
    m.box("ktx_white", (0, mid, 3.65), (2.95, length, .18))
    at=placer(0,mid,0,0)
    door_centres=[-length/2+.9,length/2-.9]
    for sx in (-1,1):
        vehicle_side(m,at,sx*1.46,length,door_centres,.56,"ktx_white",.91,1.85,3.02,3.56,0,"ktx_blue")
    yy=body0+2.2
    while yy<body1-2:
        for xx in (-1.0,-.55,.55,1.0):
            m.box("seat_blue",(xx,yy,1.43),(.43,.64,.15))
            m.box("seat_blue",(xx,yy-.27,1.78),(.43,.12,.7))
        yy+=1.0
    m.box("ktx_grey", (0, mid, .55), (2.6, length - .6, .6))
    for end, sign in ((y1, 1), (y0, -1)):
        if (sign > 0 and nose_front) or (sign < 0 and nose_back):
            base = body1 if sign > 0 else body0
            profile = [(1.55, 0), (1.5, 1.6), (1.3, 3.2), (.95, 4.6), (.5, 5.8), (.12, 6.5)]
            m.lathe("ktx_white", [(r, h * sign) for r, h in profile], (0, base, 2.25), 20, axis="Y")
            m.box("ktx_grey", (0, base + sign * 3.2, 3.15), (1.7, 2.6, .05), rot_x=-.32 * sign)
            m.box("ktx_blue", (0, base + sign * 5.2, 1.6), (1.0, 1.4, .2))
            m.box("light", (.55, base + sign * 5.5, 1.75), (.3, .05, .12))
            m.box("light", (-.55, base + sign * 5.5, 1.75), (.3, .05, .12))
    for bogie in (body0 + 2.6, body1 - 2.6):
        for dd in (-1.25, 1.25):
            for sx in (-.75, .75):
                m.cylinder("rubber", (sx, bogie + dd, .45), .45, .18, 14, axis="X")
    m.box("metal", (0, mid, 3.75), (1.8, length * .9, .12))


def build_ktx():
    """Three-car KTX-Sancheon style set (front power car, saloon, rear power car); ~58 m."""
    m = Model("Ktx")
    ktx_car(m, 9.6, 29.0, nose_front=True)
    ktx_car(m, -9.4, 9.4)
    ktx_car(m, -29.0, -9.6, nose_back=True)
    m.box("darkmetal", (0, 9.5, 2.0), (2.6, .5, 2.6))
    m.box("darkmetal", (0, -9.5, 2.0), (2.6, .5, 2.6))
    pantograph_y = -20.0
    m.box("metal", (0, pantograph_y, 4.25), (.08, 1.8, .08), rot_x=.6)
    m.box("metal", (0, pantograph_y + .4, 4.75), (1.6, .1, .06))
    return m.finish()


def build_airplane():
    """Generic twin-engine narrow-body airliner (~44 m long, 36 m span), nose toward +Y, no airline marks."""
    m = Model("Airplane")
    r, z = 2.0, 3.9
    m.lathe("plane_white", [(r, -15), (r, 14)], (0, 0, z), 20, cap=False, axis="Y")
    m.lathe("plane_white", [(r, 0), (1.85, 1.6), (1.5, 3.0), (.9, 4.2), (.2, 5.0)], (0, 14, z), 20, axis="Y")
    m.lathe("plane_white", [(r, 0), (1.6, -3.5), (1.0, -7.0), (.35, -9.0)], (0, -15, z + .35), 20, axis="Y")
    m.box("plane_belly", (0, 0, z - 1.55), (2.6, 28, 1.0))
    m.box("glass", (0, 16.2, z + .85), (1.5, 1.3, .35), rot_x=-.6)
    for sx in (-1.99, 1.99):
        k = -12.0
        while k < 12.5:
            m.box("darkmetal", (sx, k, z + .55), (.03, .28, .35))
            k += .82
        m.box("darkmetal", (sx, 12.2, z), (.03, .9, 1.8))
        m.box("darkmetal", (sx, -12.6, z), (.03, .8, 1.7))
    for sx in (-1, 1):
        m.plate("plane_white", [(sx * 1.6, 4.0, z - 1.2), (sx * 17.6, -6.4, z - .1), (sx * 17.6, -8.0, z - .1),
                                (sx * 1.6, -3.0, z - 1.2)], .35)
        m.plate("plane_white", [(sx * 17.5, -6.6, z - .1), (sx * 17.8, -7.6, z + 1.8), (sx * 17.8, -8.3, z + 1.8),
                                (sx * 17.5, -8.0, z - .1)], .15)
        m.cylinder("plane_belly", (sx * 5.7, 2.6, z - 2.0), .95, 3.6, 18, axis="Y")
        m.cylinder("darkmetal", (sx * 5.7, 4.42, z - 2.0), .78, .06, 18, axis="Y")
        m.box("plane_white", (sx * 5.7, 1.6, z - 1.25), (.3, 2.6, .7))
        m.plate("plane_white", [(sx * .8, -17.0, z + 1.0), (sx * 6.6, -21.6, z + 1.3), (sx * 6.6, -22.8, z + 1.3),
                                (sx * .8, -21.2, z + 1.0)], .22)
    m.plate("tail_blue", [(0, -16.2, z + 1.7), (0, -21.6, z + 8.2), (0, -23.4, z + 8.2), (0, -22.4, z + 1.2)], .4)
    m.box("darkmetal", (0, 12.0, .9), (.25, .25, 1.8))
    m.cylinder("rubber", (0, 12.0, .38), .38, .5, 12, axis="X")
    for sx in (-2.9, 2.9):
        m.box("darkmetal", (sx, -1.0, 1.1), (.25, .25, 2.2))
        m.cylinder("rubber", (sx, -1.0, .55), .55, .8, 12, axis="X")
    return m.finish()


def build_fare_gate():
    """Subway fare gate cabinet (1.6 m long), card reader on top facing +Y entry side."""
    m = Model("FareGate")
    m.box("chrome", (0, 0, .5), (.28, 1.6, 1.0))
    m.box("darkmetal", (0, 0, 1.01), (.3, 1.62, .04))
    m.box("reader", (0, .45, 1.05), (.24, .3, .05))
    m.box("screen", (0, -.1, 1.07), (.18, .22, .06), rot_x=.4)
    m.box("green", (0, .78, .95), (.2, .03, .1))
    return m.finish()


def build_ticket_machine():
    """Transit card top-up machine, screen toward -Y."""
    m = Model("TicketMachine")
    m.box("steel_blue", (0, 0, .95), (1.0, .7, 1.9))
    m.box("screen", (0, -.36, 1.3), (.6, .04, .45))
    m.box("reader", (.3, -.36, .95), (.2, .05, .15))
    m.box("darkmetal", (-.2, -.36, .75), (.3, .05, .08))
    m.box("white", (0, -.36, 1.75), (.9, .04, .18))
    return m.finish()


BUILDERS = [
    build_camp, build_pit_house, build_farm, build_forge, build_market, build_workshop, build_kiln, build_hanok,
    build_school, build_railworks, build_house, build_coal_power, build_power, build_oil_power, build_factory,
    build_apartment, build_nuclear, build_wind, build_bus_depot, build_metro_depot,
    build_bus, build_metro, build_car, build_bus_cabin, build_metro_cabin, build_tree, build_pine,
    build_street_light, build_traffic_light, build_road,
    build_person_body, build_person_leg, build_person_arm, build_door_frame, build_door_leaf,
    build_bus_blue, build_bus_red, build_bus_yellow, build_ktx, build_airplane, build_fare_gate, build_ticket_machine,
] + [lambda n=n, b=b: build_car(n, b) for n, b in CAR_VARIANTS]


def main():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    x = 0.0
    for builder in BUILDERS:
        obj = builder()
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, obj.name + ".fbx"), use_selection=True,
                                 object_types={"MESH"}, apply_unit_scale=True, axis_forward="-Z", axis_up="Y",
                                 add_leaf_bones=False, mesh_smooth_type="FACE")
        obj.location.x = x
        x += 14
        print("EXPORTED", obj.name, len(obj.data.polygons), "faces", len(obj.data.materials), "materials")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "AssetSources", "CityModels.blend"))


main()
