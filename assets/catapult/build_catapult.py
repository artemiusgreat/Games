"""Build a Catapult-style heavy mech in Blender, rigged, clay presentation.

Procedural hard-surface build: beveled primitives, parts as separate named
objects, rigid-bound to an armature (pelvis/torso/doors/hips/knees/ankles).
Exports FBX (rigged) + OBJ (static), saves .blend, renders verification
frames (front / front-3/4 / rear-3/4) in the reference's gray-clay style.

Run: blender -b --python build_catapult.py
Env: MECH_FINAL=1 -> higher-quality final renders.
Forward = +Y. 1 unit = 1 m. Feet on z=0, total height ~12.6 with doors open.
"""
import bpy
import os
import math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
FINAL = os.environ.get("MECH_FINAL", "0") == "1"

# ----------------------------------------------------------------------------
# clean slate + materials
# ----------------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

CLAY = bpy.data.materials.new("ClayGray")
CLAY.use_nodes = True
CLAY.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.46, 0.46, 0.48, 1)
CLAY.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9

DARK = bpy.data.materials.new("DarkInset")
DARK.use_nodes = True
DARK.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.13, 0.13, 0.14, 1)
DARK.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.95

WHITE = bpy.data.materials.new("StudioWhite")
WHITE.use_nodes = True
WHITE.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (1, 1, 1, 1)
WHITE.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 1.0

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
        mat=CLAY, bone=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius,
                                        depth=depth, location=loc, rotation=rot)
    obj = bpy.context.active_object
    return _finish(obj, name, mat, bevel, bone)


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
    obj.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(direction.normalized())
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_mode = "XYZ"
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
DOOR_DIR = Vector((0, math.cos(math.radians(52)), math.sin(math.radians(52))))

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
    door_b[tag] = bone(f"door_{tag}", DOOR_HINGE[s], DOOR_HINGE[s] + DOOR_DIR * 3.35, torso_b)
    hip_b[tag] = bone(f"hip_{tag}", HIP[s]["hip"], HIP[s]["knee"], pelvis_b)
    knee_b[tag] = bone(f"knee_{tag}", HIP[s]["knee"], HIP[s]["ankle"], hip_b[tag])
    bone(f"ankle_{tag}", HIP[s]["ankle"], HIP[s]["toe"], knee_b[tag])
bpy.ops.object.mode_set(mode="OBJECT")

# ----------------------------------------------------------------------------
# center body
# ----------------------------------------------------------------------------
box("pelvis", (2.30, 1.80, 1.05), (0, -0.05, 5.85), bevel=0.07, bone="pelvis",
    taper_top=(0.92, 0.92))
box("pelvis_rear", (1.90, 0.55, 0.80), (0, -1.02, 5.80), bevel=0.05, bone="pelvis")
box("torso_lower", (2.95, 2.10, 1.00), (0, 0.0, 6.90), bevel=0.06, bone="torso")

# pelvis front grill (narrow vent block, dark insets, one joined object)
slats = []
for i in range(3):
    slats.append(box(f"_grill{i}", (1.15, 0.08, 0.11), (0, 0.88, 5.62 + i * 0.20),
                     bevel=0.01, mat=DARK, bone="pelvis"))
join_objects("pelvis_grill", slats, "pelvis")

# cab: one leaning wedge (sloped front is the cab's own face) + nose + brow
box("cab", (3.35, 2.80, 2.40), (0, -0.25, 8.28), bevel=0.08, bone="torso",
    taper_top=(0.94, 0.95), lean=0.38)
box("cab_nose", (1.30, 0.75, 0.85), (0, 1.12, 7.62), rot=(math.radians(24), 0, 0),
    bevel=0.06, bone="torso")
box("cab_chin", (1.00, 0.55, 0.50), (0, 1.15, 7.02), bevel=0.05, bone="torso")
box("cab_prow", (0.34, 0.10, 1.50), (0, 0.58, 8.55), rot=(math.radians(24), 0, 0),
    bevel=0.02, bone="torso")
box("cab_sensor", (1.30, 0.12, 0.30), (0, 0.42, 8.98),
    rot=(math.radians(24), 0, 0), bevel=0.02, mat=DARK, bone="torso")
box("cab_brow", (2.90, 2.20, 0.35), (0, -0.10, 9.50), bevel=0.05, bone="torso")

# roof gear: antenna + locator
cyl("antenna_base", 0.07, 0.18, (-0.72, -0.35, 9.48), bevel=0.01, bone="torso")
cyl("antenna", 0.028, 1.70, (-0.72, -0.35, 10.25), bevel=0.0, bone="torso")
box("locator_mast", (0.09, 0.09, 0.40), (0.78, -0.35, 9.62), bevel=0.01, bone="torso")
box("locator", (0.42, 0.20, 0.50), (0.78, -0.35, 9.98), bevel=0.03, bone="torso")

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
        rot=(0, math.radians(90), 0), bevel=0.03, mat=DARK, bone="torso")

# ----------------------------------------------------------------------------
# per-side assemblies: missile pod, door, tubes, laser, leg
# ----------------------------------------------------------------------------
for s, tag in ((1, "L"), (-1, "R")):
    px = s * 2.95  # pod center x

    # missile pod: big deep box + proud side panel + outer rear cap disc
    box(f"rocket_module_{tag}", (2.20, 3.60, 3.10), (px, -0.45, 8.45),
        bevel=0.07, bone="torso")
    box(f"pod_panel_{tag}", (0.08, 2.60, 2.20), (s * 4.07, -0.45, 8.45),
        bevel=0.02, bone="torso")
    cyl(f"pod_cap_{tag}", 0.60, 0.30, (s * 4.10, -0.95, 7.70),
        rot=(0, math.radians(90), 0), bevel=0.03, bone="torso")

    # tube face plate + 15 dark tube mouths (3 cols x 5 rows), joined as one
    box(f"tube_plate_{tag}", (1.85, 0.14, 2.70), (px, 1.40, 8.45),
        bevel=0.02, bone="torso")
    tubes = []
    for col in range(3):
        for row in range(5):
            tubes.append(cyl(f"_tube{tag}{col}{row}", 0.155, 0.22,
                             (px + (col - 1) * 0.55, 1.50, 8.45 + (row - 2) * 0.52),
                             rot=(math.radians(90), 0, 0), vertices=16,
                             bevel=0.005, mat=DARK, bone="torso"))
    join_objects(f"tubes_{tag}", tubes, "torso")

    # door: panel built OPEN (~52 deg), origin on the hinge line
    hinge = DOOR_HINGE[s]
    center = hinge + DOOR_DIR * 1.675 + Vector((0, 0, 0.09))
    door = box(f"door_{tag}", (2.15, 3.35, 0.16), tuple(center),
               rot=(math.radians(52), 0, 0), bevel=0.03, bone=f"door_{tag}")
    # door underside rib
    rib_c = hinge + DOOR_DIR * 1.675 + Vector((0, 0, -0.16))
    box(f"door_rib_{tag}", (1.80, 2.90, 0.10), tuple(rib_c),
        rot=(math.radians(52), 0, 0), bevel=0.02, bone=f"door_{tag}")

    # laser gun stub under the pod / beside lower torso: mount + twin barrels
    parts = [box(f"_lgmount{tag}", (0.30, 0.50, 0.35), (s * 1.72, 0.95, 6.95),
                 bevel=0.03, bone="torso")]
    for dx in (-0.09, 0.09):
        parts.append(cyl(f"_lgbar{tag}{dx}", 0.06, 0.75, (s * 1.72 + dx, 1.45, 6.90),
                         rot=(math.radians(90), 0, 0), vertices=12, bevel=0.005,
                         mat=DARK, bone="torso"))
    join_objects(f"laser_gun_{tag}", parts, "torso")

    # --- leg: hip disc, thigh + front plate, knee, shin + guard, ankle, foot ---
    J = HIP[s]
    cyl(f"hip_joint_{tag}", 0.62, 0.60, (s * 1.25, -0.05, 5.80),
        rot=(0, math.radians(90), 0), bevel=0.03, mat=DARK, bone=f"hip_{tag}")
    limb_box(f"upper_leg_{tag}", (s * 1.02, -0.05, 6.05), tuple(J["knee"]),
             1.22, 1.42, bevel=0.07, bone=f"hip_{tag}", taper_bottom=(0.85, 0.88))
    limb_box(f"thigh_plate_{tag}", (s * 1.04, 0.62, 5.70), (s * 1.13, 1.05, 3.80),
             1.05, 0.28, bevel=0.05, bone=f"hip_{tag}")

    cyl(f"knee_joint_{tag}", 0.38, 1.15, tuple(J["knee"]),
        rot=(0, math.radians(90), 0), bevel=0.03, mat=DARK, bone=f"knee_{tag}")
    box(f"knee_cap_{tag}", (0.85, 0.55, 0.80), (s * 1.12, 0.92, 3.35),
        bevel=0.05, bone=f"knee_{tag}")
    limb_box(f"lower_leg_{tag}", tuple(J["knee"]), (s * 1.12, -0.32, 0.70),
             0.95, 1.05, bevel=0.06, bone=f"knee_{tag}", taper_top=(0.88, 0.9))
    # armored shin guard parallel to the shin axis, offset along its front
    # normal (axis knee->ankle, front normal perpendicular in the YZ plane)
    _k, _a = Vector((s * 1.12, 0.60, 3.35)), Vector((s * 1.12, -0.32, 0.70))
    _d = (_a - _k).normalized()
    _n = Vector((0, -_d.z, _d.y))  # front-facing perpendicular
    if _n.y < 0:
        _n = -_n
    limb_box(f"leg_protection_{tag}",
             tuple(_k + (_a - _k) * 0.12 + _n * 0.58),
             tuple(_k + (_a - _k) * 0.72 + _n * 0.58),
             0.95, 0.22, bevel=0.05, bone=f"knee_{tag}")
    limb_box(f"shin_inset_{tag}",
             tuple(_k + (_a - _k) * 0.25 + _n * 0.70),
             tuple(_k + (_a - _k) * 0.60 + _n * 0.70),
             0.55, 0.08, bevel=0.02, mat=DARK, bone=f"knee_{tag}")

    cyl(f"ankle_joint_{tag}", 0.28, 0.80, tuple(J["ankle"]),
        rot=(0, math.radians(90), 0), bevel=0.02, mat=DARK, bone=f"ankle_{tag}")
    foot_parts = [
        box(f"_footbase{tag}", (1.00, 1.90, 0.42), (s * 1.12, 0.22, 0.21),
            bevel=0.04, bone=f"ankle_{tag}"),
        box(f"_heel{tag}", (0.90, 0.70, 0.60), (s * 1.12, -0.50, 0.32),
            bevel=0.04, bone=f"ankle_{tag}"),
    ]
    for dx in (-0.26, 0.26):
        foot_parts.append(box(f"_toe{tag}{dx}", (0.40, 0.95, 0.26),
                              (s * 1.12 + dx, 1.32, 0.13),
                              rot=(math.radians(-7), 0, 0), bevel=0.03,
                              bone=f"ankle_{tag}"))
    foot_parts.append(box(f"_rtoe{tag}", (0.45, 0.75, 0.26), (s * 1.12, -1.00, 0.13),
                          rot=(math.radians(7), 0, 0), bevel=0.03,
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
# save .blend (mech only), then exports
# ----------------------------------------------------------------------------
blend_path = os.path.join(HERE, "catapult.blend")
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

# poly count on evaluated meshes
dg = bpy.context.evaluated_depsgraph_get()
tris = 0
for obj in MECH_OBJECTS:
    eo = obj.evaluated_get(dg)
    eo.data.calc_loop_triangles()
    tris += len(eo.data.loop_triangles)
print(f"POLYCOUNT evaluated triangles: {tris}")

# ----------------------------------------------------------------------------
# studio: ground, lights, cameras (created after exports so they stay out)
# ----------------------------------------------------------------------------
bpy.ops.mesh.primitive_circle_add(vertices=48, radius=40.0, fill_type="NGON",
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

area_light("KeyLight", (9, 11, 15), 1500, 9)
area_light("FillLight", (-11, -7, 9), 600, 10)

def camera(name, loc, target):
    data = bpy.data.cameras.new(name=name)
    o = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(o)
    o.location = loc
    d = Vector(target) - o.location
    o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    return o

cam_front = camera("CamFront", (0, 19.5, 6.8), (0, 0, 6.1))
cam_34 = camera("Cam34", (12.5, 14.5, 8.4), (0, 0, 5.9))
cam_rear = camera("CamRear", (-10.5, -16.0, 7.6), (0, 0, 6.0))

world = bpy.data.worlds.new("StudioWorld")
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
bg.inputs["Color"].default_value = (1, 1, 1, 1)
bg.inputs["Strength"].default_value = 0.7

scene.render.engine = "CYCLES"
scene.cycles.samples = 96 if FINAL else 32
scene.cycles.use_denoising = FINAL
scene.render.resolution_x = 900 if FINAL else 640
scene.render.resolution_y = 900 if FINAL else 640
scene.render.image_settings.file_format = "PNG"

for cam, fname in ((cam_front, "render_front.png"),
                   (cam_34, "render_34.png"),
                   (cam_rear, "render_rear.png")):
    if fname == "render_rear.png" and not FINAL:
        continue
    scene.camera = cam
    scene.render.filepath = os.path.join(HERE, fname)
    bpy.ops.render.render(write_still=True)
    print("RENDERED:", fname)

# re-save the .blend with the studio included for re-rendering
bpy.ops.wm.save_as_mainfile(filepath=blend_path)
print("DONE")
