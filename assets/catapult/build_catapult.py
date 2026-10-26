"""Build a Catapult-style heavy mech in Blender, rigged, clay presentation.

PASS 2 — fidelity iteration: sculpted lofted cab with layered plates and chin
cluster, recessed framed tube panels with real tube rims, thick doors with
inset panels + ribs (leaning back past vertical, as in the reference),
armored legs with shell plates, hydraulic pistons, slatted shin guards and
segmented toes, torso/pelvis greebles. Studio final render matches the
reference photo view (front-left 3/4, white bg, soft shadow).

Procedural hard-surface build: beveled primitives + lofted profiles, parts
as separate named objects, rigid-bound to an armature
(pelvis/torso/doors/hips/knees/ankles). Exports FBX (rigged) + OBJ (static),
saves .blend, renders verification frames.

Run: blender -b --python build_catapult.py
Env: MECH_FINAL=1   -> high-quality final renders (default behavior too;
                       finals are what get written to render_*.png)
     MECH_PREVIEW=1 -> build + one quick photo-view render to /tmp/preview.png
Forward = +Y. 1 unit = 1 m. Feet on z=0, total height ~13.2 with doors open.
"""
import bpy
import bmesh
import os
import math
from mathutils import Vector, Quaternion

HERE = os.path.dirname(os.path.abspath(__file__))
PREVIEW = os.environ.get("MECH_PREVIEW", "0") == "1"

# ----------------------------------------------------------------------------
# clean slate + materials
# ----------------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def _mat(name, color, rough, metallic=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metallic
    return m


CLAY = _mat("ClayGray", (0.35, 0.35, 0.37), 0.9)
CLAY2 = _mat("ClayDark", (0.272, 0.272, 0.29), 0.92)   # second clay tone
DARK = _mat("DarkInset", (0.10, 0.10, 0.11), 0.95)      # insets / interiors
METAL = _mat("PistonMetal", (0.55, 0.56, 0.58), 0.32, metallic=0.9)
STEEL = _mat("DarkSteel", (0.22, 0.22, 0.24), 0.5, metallic=0.7)

WHITE = bpy.data.materials.new("StudioWhite")
WHITE.use_nodes = True
_bsdf = WHITE.node_tree.nodes["Principled BSDF"]
_bsdf.inputs["Base Color"].default_value = (1, 1, 1, 1)
_bsdf.inputs["Roughness"].default_value = 1.0
_emis = WHITE.node_tree.nodes.new("ShaderNodeEmission")
_emis.inputs["Color"].default_value = (1, 1, 1, 1)
_emis.inputs["Strength"].default_value = 0.55
_mix = WHITE.node_tree.nodes.new("ShaderNodeMixShader")
_mix.inputs[0].default_value = 0.72
WHITE.node_tree.links.new(_bsdf.outputs[0], _mix.inputs[1])
WHITE.node_tree.links.new(_emis.outputs[0], _mix.inputs[2])
WHITE.node_tree.links.new(_mix.outputs[0],
                          WHITE.node_tree.nodes["Material Output"].inputs[0])

MECH_OBJECTS = []   # every mech mesh object, in creation order
BINDINGS = {}       # object name -> bone name


def _finish(obj, name, mat, bevel, bone):
    obj.name = name
    if mat:
        obj.data.materials.append(mat)
    if bevel and bevel > 0:
        mod = obj.modifiers.new(name="bev", type="BEVEL")
        mod.width = bevel
        mod.segments = 2
    MECH_OBJECTS.append(obj)
    if bone:
        BINDINGS[name] = bone
    return obj


def box(name, dims, loc, rot=(0.0, 0.0, 0.0), bevel=0.05, mat=CLAY, bone=None,
        taper_top=None, taper_bottom=None, lean=0.0):
    """Axis box; taper scales local +/-z vertex layers, lean shifts the top
    layer in -y (local units, pre-scale) for sloped/wedge fronts."""
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
    obj = bpy.context.active_object
    if taper_top is not None or taper_bottom is not None or lean:
        for v in obj.data.vertices:
            if v.co.z > 0:
                if taper_top is not None:
                    v.co.x *= taper_top[0]
                    v.co.y *= taper_top[1]
                v.co.y -= lean
            if v.co.z < 0 and taper_bottom is not None:
                v.co.x *= taper_bottom[0]
                v.co.y *= taper_bottom[1]
    obj.dimensions = dims
    obj.location = loc
    obj.rotation_euler = rot
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return _finish(obj, name, mat, bevel, bone)


def cyl(name, radius, depth, loc, rot=(0.0, 0.0, 0.0), vertices=24, bevel=0.02,
        mat=CLAY, bone=None, open_ended=False):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth, location=loc,
        rotation=rot, end_fill_type="NOTHING" if open_ended else "NGON")
    obj = bpy.context.active_object
    return _finish(obj, name, mat, bevel, bone)


def tube(name, a, b, radius, mat=CLAY, bone=None, vertices=14, bevel=0.0):
    """Cylinder whose axis runs from point a to point b (pistons, rails)."""
    a, b = Vector(a), Vector(b)
    mid = (a + b) * 0.5
    direction = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius,
                                        depth=direction.length,
                                        location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.location = mid
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(
        direction.normalized())
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_mode = "XYZ"
    return _finish(obj, name, mat, bevel, bone)


def piston(name, a, b, bone, r_sleeve=0.10, r_rod=0.055):
    """Hydraulic piston: dark sleeve from a (55%), bright rod from b (55%)."""
    a, b = Vector(a), Vector(b)
    mid1 = a + (b - a) * 0.55
    mid2 = b + (a - b) * 0.55
    s = tube(f"_{name}_sleeve", tuple(a), tuple(mid1), r_sleeve,
             mat=STEEL, bone=bone, vertices=12)
    r = tube(f"_{name}_rod", tuple(mid2), tuple(b), r_rod,
             mat=METAL, bone=bone, vertices=10)
    return join_objects(name, [s, r], bone)


def limb_box(name, a, b, w, d, bevel=0.05, mat=CLAY, bone=None,
             taper_top=None, taper_bottom=None):
    """Box whose local +Z runs from point a to point b (segment armor)."""
    a, b = Vector(a), Vector(b)
    mid = (a + b) * 0.5
    direction = b - a
    length = direction.length
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
    obj = bpy.context.active_object
    if taper_top is not None or taper_bottom is not None:
        for v in obj.data.vertices:
            if v.co.z > 0 and taper_top is not None:
                v.co.x *= taper_top[0]
                v.co.y *= taper_top[1]
            if v.co.z < 0 and taper_bottom is not None:
                v.co.x *= taper_bottom[0]
                v.co.y *= taper_bottom[1]
    obj.dimensions = (w, d, length)
    obj.location = mid
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(
        direction.normalized())
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_mode = "XYZ"
    return _finish(obj, name, mat, bevel, bone)


def loft(name, sections, mat=CLAY, bone=None, bevel=0.03, chamfer=0.30):
    """Loft a hard-surface form through horizontal sections.

    sections: list of (z, half_width, y_front, y_back). Each ring is a
    chamfered rectangle (8 points); rings are skinned with quads and the
    ends capped. Gives tapered, sculpted volumes (the cab)."""
    rings = []
    for (z, hw, yf, yb) in sections:
        c = min(chamfer, hw * 0.45)
        rings.append([
            (-hw + c, yf, z), (hw - c, yf, z), (hw, yf - c, z),
            (hw, yb + c, z), (hw - c, yb, z), (-hw + c, yb, z),
            (-hw, yb + c, z), (-hw, yf - c, z),
        ])
    verts = [p for ring in rings for p in ring]
    faces = []
    n = 8
    for i in range(len(rings) - 1):
        for j in range(n):
            j2 = (j + 1) % n
            faces.append((i * n + j, i * n + j2,
                          (i + 1) * n + j2, (i + 1) * n + j))
    faces.append(tuple(reversed(range(n))))                      # bottom cap
    top0 = (len(rings) - 1) * n
    faces.append(tuple(range(top0, top0 + n)))                   # top cap
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(verts, [], faces)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return _finish(obj, name, mat, bevel, bone)


def join_objects(name, objs, bone):
    """Join several created objects into one named object bound to `bone`."""
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
        MECH_OBJECTS.remove(o)
        BINDINGS.pop(o.name, None)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    MECH_OBJECTS.append(obj)
    if bone:
        BINDINGS[name] = bone
    return obj


# ----------------------------------------------------------------------------
# armature first (bones only need joint positions; meshes bind afterwards)
# ----------------------------------------------------------------------------
HIP = {}   # side sign -> dict of joint points
for s in (1, -1):
    HIP[s] = {
        "hip":   Vector((s * 0.98, -0.05, 5.80)),
        "knee":  Vector((s * 1.12,  0.60, 3.35)),
        "ankle": Vector((s * 1.12, -0.32, 0.85)),
        "toe":   Vector((s * 1.12,  0.55, 0.20)),
    }
DOOR_HINGE = {s: Vector((s * 2.95, -2.12, 10.02)) for s in (1, -1)}
# doors hinge at the rear top edge and open leaning BACK ~20 deg past vertical
DOOR_DIR = Vector((0, -0.342, 0.940))
DOOR_LEN = 3.35

arm_data = bpy.data.armatures.new("CatapultRigData")
ARM = bpy.data.objects.new("CatapultRig", arm_data)
bpy.context.collection.objects.link(ARM)
bpy.context.view_layer.objects.active = ARM
bpy.ops.object.mode_set(mode="EDIT")

def bone(name, head, tail, parent=None):
    b = arm_data.edit_bones.new(name)
    b.head = head
    b.tail = tail
    if parent:
        b.parent = parent
    return b

pelvis_b = bone("pelvis", (0, 0, 4.95), (0, 0, 6.35))
torso_b = bone("torso", (0, 0, 6.35), (0, 0, 9.30), pelvis_b)
door_b = {}
hip_b, knee_b = {}, {}
for s, tag in ((1, "L"), (-1, "R")):
    door_b[tag] = bone(f"door_{tag}", DOOR_HINGE[s],
                       DOOR_HINGE[s] + DOOR_DIR * DOOR_LEN, torso_b)
    hip_b[tag] = bone(f"hip_{tag}", HIP[s]["hip"], HIP[s]["knee"], pelvis_b)
    knee_b[tag] = bone(f"knee_{tag}", HIP[s]["knee"], HIP[s]["ankle"], hip_b[tag])
    bone(f"ankle_{tag}", HIP[s]["ankle"], HIP[s]["toe"], knee_b[tag])
bpy.ops.object.mode_set(mode="OBJECT")

# ----------------------------------------------------------------------------
# center body: pelvis, torso, sculpted cab
# ----------------------------------------------------------------------------
box("pelvis", (2.30, 1.80, 1.05), (0, -0.05, 5.85), bevel=0.07, bone="pelvis",
    taper_top=(0.92, 0.92))
box("pelvis_rear", (1.90, 0.55, 0.80), (0, -1.02, 5.80), bevel=0.05, bone="pelvis")
box("torso_lower", (2.95, 2.10, 1.00), (0, 0.0, 6.90), bevel=0.06, bone="torso")
# chest plate under the cab's chin
box("torso_chest", (2.20, 0.28, 0.95), (0, 0.98, 6.72),
    rot=(math.radians(10), 0, 0), bevel=0.05, mat=CLAY2, bone="torso")

# pelvis front grill: dark recess panel + clay slats (wide, as in reference)
box("pelvis_grill_panel", (1.55, 0.10, 0.80), (0, 0.90, 5.82),
    bevel=0.02, mat=DARK, bone="pelvis")
slats = []
for i in range(4):
    slats.append(box(f"_grill{i}", (1.38, 0.06, 0.10),
                     (0, 0.96, 5.56 + i * 0.185), bevel=0.01, bone="pelvis"))
join_objects("pelvis_grill", slats, "pelvis")
# pelvis side cheek plates
for s in (1, -1):
    box(f"pelvis_plate_{'L' if s > 0 else 'R'}", (0.55, 1.15, 0.75),
        (s * 1.08, 0.42, 5.85), bevel=0.05, mat=CLAY2, bone="pelvis")
# rear access ladder (from the reference's railing): rails + rungs, joined
ladder = []
for sx in (-0.42, 0.42):
    ladder.append(box(f"_rail{sx}", (0.07, 0.07, 1.30), (sx, -1.32, 5.72),
                      bevel=0.01, mat=STEEL, bone="pelvis"))
for i in range(4):
    ladder.append(box(f"_rung{i}", (0.84, 0.06, 0.07), (0, -1.32, 5.24 + i * 0.32),
                      bevel=0.01, mat=STEEL, bone="pelvis"))
join_objects("pelvis_ladder", ladder, "pelvis")

# torso sidewalls filling the gap toward the pods, with dark vent insets
for s, tag in ((1, "L"), (-1, "R")):
    box(f"sidewall_{tag}", (0.30, 1.55, 1.30), (s * 1.60, -0.05, 7.55),
        rot=(0, 0, math.radians(-4 * s)), bevel=0.05, mat=CLAY2, bone="torso")
    for i in range(2):
        box(f"sidewall_vent_{tag}{i}", (0.06, 0.85, 0.16),
            (s * 1.77, -0.05, 7.85 - i * 0.45), bevel=0.01, mat=DARK,
            bone="torso")

# --- THE CAB: lofted sculpted head -------------------------------------------
loft("cab", [
    (6.82, 0.85, 1.34, -1.02),
    (7.28, 1.28, 1.68, -1.26),
    (7.85, 1.55, 1.82, -1.42),
    (8.45, 1.64, 1.66, -1.48),
    (9.02, 1.55, 1.18, -1.44),
    (9.30, 1.45, 0.80, -1.38),
    (9.58, 1.15, 0.34, -1.15),
], mat=CLAY, bone="torso", bevel=0.03, chamfer=0.44)
# layered cheek plates (two layers per side, angled to the face)
for s, tag in ((1, "L"), (-1, "R")):
    box(f"cab_cheek_{tag}", (0.82, 1.10, 1.08), (s * 1.33, 1.36, 7.92),
        rot=(math.radians(8), 0, math.radians(-24 * s)), bevel=0.07,
        bone="torso")
    box(f"cab_cheek2_{tag}", (0.55, 0.72, 0.72), (s * 1.50, 1.48, 7.86),
        rot=(math.radians(8), 0, math.radians(-24 * s)), bevel=0.05,
        mat=CLAY2, bone="torso")
    # side sensor bump ("ear") + small block above it
    cyl(f"cab_ear_{tag}", 0.21, 0.18, (s * 1.66, 0.30, 8.32),
        rot=(0, math.radians(90), 0), bevel=0.02, mat=STEEL, bone="torso")
    box(f"cab_earblock_{tag}", (0.16, 0.50, 0.34), (s * 1.63, 0.34, 8.80),
        bevel=0.03, mat=CLAY2, bone="torso")
# brow visor plate sunk into the forehead slope + sensor band on the face
box("cab_brow", (2.70, 0.82, 0.22), (0, 0.82, 9.28),
    rot=(math.radians(-24), 0, 0), bevel=0.06, bone="torso")
box("cab_sensor", (1.50, 0.10, 0.30), (0, 1.62, 8.60),
    rot=(math.radians(34), 0, 0), bevel=0.02, mat=DARK, bone="torso")
# prow ridge: darker keel running brow-to-chin down the face center
box("cab_prow", (0.30, 0.12, 1.70), (0, 1.82, 8.02),
    rot=(math.radians(26), 0, 0), bevel=0.02, mat=CLAY2, bone="torso")
# dark cheek intake slits on the chamfer slopes + panel seams off the prow
for s, tag in ((1, "L"), (-1, "R")):
    box(f"cab_intake_{tag}", (0.50, 0.08, 0.55), (s * 1.18, 1.78, 8.02),
        rot=(math.radians(30), 0, math.radians(-24 * s)), bevel=0.01,
        mat=DARK, bone="torso")
    box(f"cab_seam_{tag}", (0.05, 0.05, 0.95), (s * 0.58, 1.80, 7.72),
        rot=(math.radians(26), 0, math.radians(-32 * s)), bevel=0.005,
        mat=DARK, bone="torso")
# chin cluster: block + nose wedge + twin gun barrels
box("cab_chin", (0.98, 0.62, 0.52), (0, 1.64, 7.04), bevel=0.08, bone="torso")
box("cab_nose", (0.42, 0.44, 0.55), (0, 1.66, 7.14),
    rot=(math.radians(20), 0, 0), bevel=0.05, mat=CLAY2, bone="torso")
chin_guns = []
for dx in (-0.24, 0.24):
    chin_guns.append(cyl(f"_chingun{dx}", 0.070, 0.42, (dx, 1.80, 6.98),
                         rot=(math.radians(90), 0, 0), vertices=12,
                         bevel=0.005, mat=METAL, bone="torso"))
    chin_guns.append(cyl(f"_chinmuz{dx}", 0.088, 0.06, (dx, 2.01, 6.98),
                         rot=(math.radians(90), 0, 0), vertices=12,
                         bevel=0.005, mat=DARK, bone="torso"))
join_objects("cab_chin_guns", chin_guns, "torso")

# roof gear: antenna + locator on the rear deck, deck vent block
cyl("antenna_base", 0.07, 0.16, (-0.55, -0.80, 9.62), bevel=0.01, bone="torso")
cyl("antenna", 0.026, 1.85, (-0.55, -0.80, 10.55), bevel=0.0, bone="torso")
box("locator_mast", (0.09, 0.09, 0.38), (0.70, -0.80, 9.72), bevel=0.01,
    bone="torso")
box("locator", (0.40, 0.20, 0.48), (0.70, -0.80, 10.02), bevel=0.03,
    mat=CLAY2, bone="torso")
box("deck_vent", (1.15, 0.65, 0.10), (0, -0.72, 9.56),
    rot=(math.radians(-18), 0, 0), bevel=0.02, mat=DARK, bone="torso")

# rear torso panel + octagonal hatch + rear vent slats
box("rear_panel", (3.00, 0.35, 2.20), (0, -1.85, 8.20), bevel=0.05, bone="torso")
cyl("rear_hatch", 0.80, 0.18, (0, -2.06, 8.15), rot=(math.radians(90), 0, 0),
    vertices=8, bevel=0.02, bone="torso")
rslats = []
for i in range(3):
    rslats.append(box(f"_rvent{i}", (0.90, 0.06, 0.14), (0, -2.05, 8.98 - i * 0.24),
                      bevel=0.01, mat=DARK, bone="torso"))
join_objects("rear_vents", rslats, "torso")

# shoulder joint discs between torso and pods
for s, tag in ((1, "L"), (-1, "R")):
    cyl(f"shoulder_{tag}", 0.58, 0.50, (s * 1.85, -0.20, 8.50),
        rot=(0, math.radians(90), 0), bevel=0.03, mat=STEEL, bone="torso")

# ----------------------------------------------------------------------------
# per-side assemblies: missile pod, door, tubes, laser, leg
# ----------------------------------------------------------------------------
DOOR_THETA = math.atan2(DOOR_DIR.z, DOOR_DIR.y)   # rotX that lays box +Y on DIR
DOOR_U = Vector((0, DOOR_DIR.z, -DOOR_DIR.y))     # underside-facing normal

for s, tag in ((1, "L"), (-1, "R")):
    px = s * 2.95  # pod center x

    # --- missile pod body ---
    box(f"rocket_module_{tag}", (2.20, 3.60, 3.10), (px, -0.45, 8.45),
        bevel=0.09, bone="torso")
    # chamfer plates on the front top/bottom edges
    box(f"pod_chamfer_top_{tag}", (2.24, 0.55, 0.32), (px, 1.24, 9.90),
        rot=(math.radians(38), 0, 0), bevel=0.04, bone="torso")
    box(f"pod_chamfer_bot_{tag}", (2.24, 0.55, 0.32), (px, 1.24, 7.00),
        rot=(math.radians(-38), 0, 0), bevel=0.04, bone="torso")
    # framed side panel (dark seam frame + slightly proud inner plate)
    side_parts = [
        box(f"_sideframe{tag}", (0.06, 2.50, 2.30), (s * 4.065, -0.28, 8.45),
            bevel=0.01, mat=DARK, bone="torso"),
        box(f"_sideinner{tag}", (0.06, 2.24, 2.02), (s * 4.085, -0.28, 8.45),
            bevel=0.02, mat=CLAY2, bone="torso"),
    ]
    join_objects(f"pod_panel_{tag}", side_parts, "torso")
    # framed rear panel
    rear_parts = [
        box(f"_rearframe{tag}", (1.95, 0.06, 2.50), (px, -2.265, 8.45),
            bevel=0.01, mat=DARK, bone="torso"),
        box(f"_rearinner{tag}", (1.70, 0.06, 2.24), (px, -2.29, 8.45),
            bevel=0.02, mat=CLAY2, bone="torso"),
    ]
    join_objects(f"pod_rear_panel_{tag}", rear_parts, "torso")
    # horizontal seam strip low on the outer side
    box(f"pod_seam_{tag}", (0.04, 3.62, 0.07), (s * 4.06, -0.45, 7.52),
        bevel=0.005, mat=DARK, bone="torso")
    # exhaust cap on the outer side, lower rear: big disc + dark inner disc
    cyl(f"pod_cap_{tag}", 0.55, 0.30, (s * 4.10, -1.50, 7.52),
        rot=(0, math.radians(90), 0), bevel=0.03, bone="torso")
    cyl(f"pod_cap_inner_{tag}", 0.34, 0.36, (s * 4.10, -1.50, 7.52),
        rot=(0, math.radians(90), 0), bevel=0.02, mat=STEEL, bone="torso")

    # --- recessed tube panel: dark floor + raised frame + rimmed tubes ---
    box(f"tube_panel_{tag}", (1.86, 0.07, 2.66), (px, 1.375, 8.45),
        bevel=0.01, mat=DARK, bone="torso")
    frame = []
    for dx in (-0.94, 0.94):
        frame.append(box(f"_fr{tag}{dx}", (0.20, 0.12, 2.88),
                         (px + dx, 1.42, 8.45), bevel=0.02, bone="torso"))
    for dz in (-1.44, 1.44):
        frame.append(box(f"_fr{tag}z{dz}", (2.08, 0.12, 0.20),
                         (px, 1.42, 8.45 + dz), bevel=0.02, bone="torso"))
    join_objects(f"tube_frame_{tag}", frame, "torso")
    tubes = []
    for col in range(3):
        for row in range(5):
            tx = px + (col - 1) * 0.56
            tz = 8.45 + (row - 2) * 0.52
            tubes.append(cyl(f"_trim{tag}{col}{row}", 0.175, 0.20,
                             (tx, 1.45, tz), rot=(math.radians(90), 0, 0),
                             vertices=16, bevel=0.0, mat=STEEL, bone="torso",
                             open_ended=True))
            tubes.append(cyl(f"_tdisc{tag}{col}{row}", 0.128, 0.03,
                             (tx, 1.385, tz), rot=(math.radians(90), 0, 0),
                             vertices=16, bevel=0.0, mat=DARK, bone="torso"))
    join_objects(f"tubes_{tag}", tubes, "torso")

    # --- door: thick panel, inset panels both faces, ribs, hinge knuckles ---
    hinge = DOOR_HINGE[s]
    C = hinge + DOOR_DIR * (DOOR_LEN * 0.5)
    drot = (DOOR_THETA, 0, 0)
    box(f"door_{tag}", (2.15, DOOR_LEN, 0.22), tuple(C), rot=drot,
        bevel=0.04, bone=f"door_{tag}")
    # underside inset (the face visible from the front when open)
    ui = C + DOOR_U * 0.135
    box(f"door_under_frame_{tag}", (1.90, 3.00, 0.05), tuple(ui), rot=drot,
        bevel=0.01, mat=DARK, bone=f"door_{tag}")
    ui2 = C + DOOR_U * 0.16
    box(f"door_under_{tag}", (1.68, 2.78, 0.05), tuple(ui2), rot=drot,
        bevel=0.02, mat=CLAY2, bone=f"door_{tag}")
    # exterior inset (visible from the rear)
    ei = C - DOOR_U * 0.135
    box(f"door_top_frame_{tag}", (1.85, 2.90, 0.04), tuple(ei), rot=drot,
        bevel=0.01, mat=DARK, bone=f"door_{tag}")
    ei2 = C - DOOR_U * 0.155
    box(f"door_top_{tag}", (1.62, 2.66, 0.04), tuple(ei2), rot=drot,
        bevel=0.02, mat=CLAY2, bone=f"door_{tag}")
    # ribs across the underside
    ribs = []
    for f in (0.22, 0.50, 0.78):
        rc = hinge + DOOR_DIR * (DOOR_LEN * f) + DOOR_U * 0.16
        ribs.append(box(f"_rib{tag}{f}", (1.74, 0.16, 0.10), tuple(rc),
                        rot=drot, bevel=0.02, bone=f"door_{tag}"))
    join_objects(f"door_ribs_{tag}", ribs, f"door_{tag}")
    # hinge knuckles on the pod (static side of the hinge)
    for dx in (-0.62, 0.62):
        cyl(f"door_hinge_{tag}{dx}", 0.11, 0.42,
            (px + dx, hinge.y, hinge.z), rot=(0, math.radians(90), 0),
            vertices=14, bevel=0.01, mat=STEEL, bone="torso")

    # --- laser gun stub under the pod: mount + twin barrels ---
    parts = [box(f"_lgmount{tag}", (0.32, 0.52, 0.38), (s * 1.74, 0.92, 6.92),
                 bevel=0.03, bone="torso")]
    for dx in (-0.10, 0.10):
        parts.append(cyl(f"_lgbar{tag}{dx}", 0.065, 0.85,
                         (s * 1.74 + dx, 1.50, 6.88),
                         rot=(math.radians(90), 0, 0), vertices=12,
                         bevel=0.005, mat=METAL, bone="torso"))
    join_objects(f"laser_gun_{tag}", parts, "torso")

    # --- leg: hip, armored thigh, knee + piston, shin + guard, ankle, foot ---
    J = HIP[s]
    cyl(f"hip_joint_{tag}", 0.68, 0.55, (s * 1.28, -0.05, 5.80),
        rot=(0, math.radians(90), 0), bevel=0.03, bone=f"hip_{tag}")
    cyl(f"hip_hub_{tag}", 0.30, 0.74, (s * 1.28, -0.05, 5.80),
        rot=(0, math.radians(90), 0), bevel=0.02, mat=STEEL, bone=f"hip_{tag}")
    limb_box(f"upper_leg_{tag}", (s * 1.02, -0.05, 6.05), tuple(J["knee"]),
             1.30, 1.50, bevel=0.07, bone=f"hip_{tag}", taper_bottom=(0.84, 0.87))
    limb_box(f"thigh_plate_{tag}", (s * 1.04, 0.58, 5.72), (s * 1.14, 1.06, 3.82),
             1.16, 0.30, bevel=0.05, bone=f"hip_{tag}")
    box(f"thigh_shell_{tag}", (0.16, 1.05, 2.05), (s * 1.72, 0.30, 4.95),
        rot=(math.radians(-15), 0, 0), bevel=0.05, mat=CLAY2, bone=f"hip_{tag}")
    box(f"thigh_rear_{tag}", (1.00, 0.20, 1.90), (s * 1.05, -0.74, 4.90),
        rot=(math.radians(-15), 0, 0), bevel=0.04, mat=CLAY2, bone=f"hip_{tag}")

    cyl(f"knee_joint_{tag}", 0.42, 1.22, tuple(J["knee"]),
        rot=(0, math.radians(90), 0), bevel=0.03, mat=STEEL, bone=f"knee_{tag}")
    box(f"knee_cap_{tag}", (0.98, 0.55, 0.88), (s * 1.12, 0.90, 3.33),
        bevel=0.06, bone=f"knee_{tag}")
    box(f"knee_plate_{tag}", (0.70, 0.20, 0.55), (s * 1.12, 1.20, 3.30),
        bevel=0.04, mat=CLAY2, bone=f"knee_{tag}")
    piston(f"knee_piston_{tag}", (s * 1.12, -0.74, 4.78), (s * 1.12, -0.82, 2.72),
           f"knee_{tag}", r_sleeve=0.105, r_rod=0.058)

    limb_box(f"lower_leg_{tag}", tuple(J["knee"]), (s * 1.12, -0.32, 0.70),
             1.02, 1.12, bevel=0.06, bone=f"knee_{tag}", taper_top=(0.88, 0.9))
    # armored shin guard parallel to the shin axis + slat insets + diamond
    _k, _a = Vector((s * 1.12, 0.60, 3.35)), Vector((s * 1.12, -0.32, 0.70))
    _d = (_a - _k).normalized()
    _n = Vector((0, -_d.z, _d.y))  # front-facing perpendicular
    if _n.y < 0:
        _n = -_n
    limb_box(f"leg_protection_{tag}",
             tuple(_k + (_a - _k) * 0.10 + _n * 0.64),
             tuple(_k + (_a - _k) * 0.80 + _n * 0.64),
             1.00, 0.24, bevel=0.05, bone=f"knee_{tag}")
    for i, f in enumerate((0.50, 0.615, 0.73, 0.845)):
        _c = _k + (_a - _k) * f + _n * 0.78
        limb_box(f"shin_slat_{tag}{i}",
                 tuple(_c + Vector((-0.36, 0, 0))), tuple(_c + Vector((0.36, 0, 0))),
                 0.13, 0.06, bevel=0.01, mat=DARK, bone=f"knee_{tag}")
    _dc = _k + (_a - _k) * 0.30 + _n * 0.77
    _dia = box(f"shin_diamond_{tag}", (0.36, 0.36, 0.06), tuple(_dc),
               bevel=0.01, mat=CLAY2, bone=f"knee_{tag}")
    _dia.rotation_mode = "QUATERNION"
    _dq = Vector((0, 0, 1)).rotation_difference(_n)
    _dia.rotation_quaternion = Quaternion(_n, math.radians(45)) @ _dq
    box(f"calf_plate_{tag}", (0.82, 0.24, 1.60), (s * 1.12, -0.51, 2.22),
        rot=(math.radians(-19), 0, 0), bevel=0.04, mat=CLAY2, bone=f"knee_{tag}")

    cyl(f"ankle_joint_{tag}", 0.30, 0.82, tuple(J["ankle"]),
        rot=(0, math.radians(90), 0), bevel=0.02, mat=STEEL, bone=f"ankle_{tag}")
    for j, dx in enumerate((-0.28, 0.28)):
        piston(f"ankle_piston_{tag}{j}", (s * 1.12 + dx, -0.80, 1.78),
               (s * 1.12 + dx, -0.74, 0.52), f"ankle_{tag}",
               r_sleeve=0.052, r_rod=0.030)

    fx = s * 1.12
    foot_parts = [
        box(f"_footbase{tag}", (1.02, 1.55, 0.38), (fx, 0.12, 0.19),
            bevel=0.04, bone=f"ankle_{tag}"),
        box(f"_heel{tag}", (0.85, 0.62, 0.55), (fx, -0.55, 0.30),
            bevel=0.04, bone=f"ankle_{tag}"),
        box(f"_ankleblock{tag}", (0.72, 0.72, 0.55), (fx, -0.26, 0.62),
            bevel=0.04, mat=CLAY2, bone=f"ankle_{tag}"),
    ]
    # 3 front toes, two segments each, splayed; 1 rear toe
    for ti, dx in enumerate((-0.36, 0.0, 0.36)):
        yaw = math.radians(-10 * (0 if dx == 0 else (1 if dx > 0 else -1)))
        foot_parts.append(box(f"_toe1_{tag}{ti}", (0.30, 0.80, 0.24),
                              (fx + dx, 1.02, 0.20),
                              rot=(math.radians(-8), 0, yaw), bevel=0.03,
                              bone=f"ankle_{tag}"))
        foot_parts.append(box(f"_toe2_{tag}{ti}", (0.26, 0.68, 0.18),
                              (fx + dx * 1.18, 1.66, 0.11),
                              rot=(math.radians(-24), 0, yaw), bevel=0.03,
                              bone=f"ankle_{tag}"))
    foot_parts.append(box(f"_rtoe{tag}", (0.42, 0.75, 0.20), (fx, -1.00, 0.12),
                          rot=(math.radians(10), 0, 0), bevel=0.03,
                          bone=f"ankle_{tag}"))
    join_objects(f"foot_{tag}", foot_parts, f"ankle_{tag}")

# ----------------------------------------------------------------------------
# rigid binding: one vertex group per part, weight 1.0, armature modifier
# ----------------------------------------------------------------------------
for obj in MECH_OBJECTS:
    bone_name = BINDINGS[obj.name]
    vg = obj.vertex_groups.new(name=bone_name)
    vg.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    mod = obj.modifiers.new(name="Armature", type="ARMATURE")
    mod.object = ARM
    obj.parent = ARM

print(f"MECH built: {len(MECH_OBJECTS)} mesh objects, "
      f"{len(arm_data.bones)} bones: {[b.name for b in arm_data.bones]}")

# ----------------------------------------------------------------------------
# save .blend (mech only), exports, poly count — all BEFORE studio objects
# exist, so the ground/lights/cameras never leak into the exports
# ----------------------------------------------------------------------------
blend_path = os.path.join(HERE, "catapult.blend")
if not PREVIEW:
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print("SAVED BLEND:", blend_path, os.path.getsize(blend_path), "bytes")

    fbx_path = os.path.join(HERE, "catapult.fbx")
    bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=False,
                             object_types={"MESH", "ARMATURE"},
                             use_mesh_modifiers=True, add_leaf_bones=False,
                             bake_anim=False)
    print("EXPORTED FBX:", fbx_path, os.path.getsize(fbx_path), "bytes")

    obj_path = os.path.join(HERE, "catapult.obj")
    bpy.ops.wm.obj_export(filepath=obj_path, apply_modifiers=True,
                          export_materials=True)
    print("EXPORTED OBJ:", obj_path, os.path.getsize(obj_path), "bytes")

    dg = bpy.context.evaluated_depsgraph_get()
    tris = 0
    for obj in MECH_OBJECTS:
        eo = obj.evaluated_get(dg)
        eo.data.calc_loop_triangles()
        tris += len(eo.data.loop_triangles)
    print(f"POLYCOUNT evaluated triangles: {tris}")

# ----------------------------------------------------------------------------
# studio: ground, lights, cameras, world
# ----------------------------------------------------------------------------
bpy.ops.mesh.primitive_circle_add(vertices=48, radius=60.0, fill_type="NGON",
                                  location=(0, 0, 0))
ground = bpy.context.active_object
ground.name = "StudioGround"
ground.data.materials.append(WHITE)

def area_light(name, loc, energy, size):
    data = bpy.data.lights.new(name=name, type="AREA")
    data.energy = energy
    data.shape = "DISK"
    data.size = size
    o = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(o)
    o.location = loc
    d = Vector((0, 0, 6)) - o.location
    o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    return o

area_light("KeyLight", (10, 12, 16), 1750, 10)
area_light("FillLight", (-12, 5, 8), 480, 12)
area_light("RimLight", (-5, -14, 14), 950, 8)

def camera(name, loc, target, lens=50):
    data = bpy.data.cameras.new(name=name)
    data.lens = lens
    o = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(o)
    o.location = loc
    d = Vector(target) - o.location
    o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    return o

# photo view matched to the reference: front-left 3/4, slightly above
cam_photo = camera("CamPhoto", (15.0, 20.5, 10.4), (0, 0, 6.2), lens=58)
cam_34 = camera("Cam34", (-16.0, 19.0, 10.2), (0, 0, 6.2), lens=55)
cam_rear = camera("CamRear", (-13.5, -19.5, 10.2), (0, 0, 6.2), lens=52)

world = bpy.data.worlds.new("StudioWorld")
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
bg.inputs["Color"].default_value = (1, 1, 1, 1)
bg.inputs["Strength"].default_value = 1.45

scene.render.engine = "CYCLES"
scene.render.image_settings.file_format = "PNG"

if PREVIEW:
    scene.cycles.samples = 20
    scene.cycles.use_denoising = False
    scene.render.resolution_x = 560
    scene.render.resolution_y = 560
    scene.camera = cam_photo
    scene.render.filepath = "/tmp/preview.png"
    bpy.ops.render.render(write_still=True)
    print("PREVIEW RENDERED: /tmp/preview.png")
else:
    scene.cycles.samples = 96
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 1000
    for cam, fname in ((cam_photo, "render_front.png"),
                       (cam_34, "render_34.png"),
                       (cam_rear, "render_rear.png")):
        scene.camera = cam
        scene.render.filepath = os.path.join(HERE, fname)
        bpy.ops.render.render(write_still=True)
        print("RENDERED:", fname)

    # re-save the .blend with the studio included for re-rendering
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print("DONE")
