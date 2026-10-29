# Decimate Poly Haven's photogrammetry scans into game-weight GLTFs.
# Run with Blender headless:
#   blender -b --python decimate_models.py
# Reads models_raw/<id>/<id>_1k.gltf, writes models/<id>/<id>.gltf.
# Per-mesh DECIMATE-collapse with a ratio that brings the model's
# total triangle count to TARGETS[id]; UVs and material slots are
# preserved by the collapse modifier. Grass is already light and is
# copied through untouched.
import os
import shutil

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
TARGETS = {
    "island_tree_01": 45000,
    "island_tree_02": 45000,
    "island_tree_03": 45000,
    "coast_rocks_01": 14000,
}


def tri_count(objs):
    total = 0
    for ob in objs:
        if ob.type == "MESH":
            total += len(ob.data.polygons)  # close enough pre-triangulation
    return total


def process(mid, target):
    src_dir = os.path.join(HERE, "models_raw", mid)
    src = None
    for f in os.listdir(src_dir):
        if f.endswith(".gltf"):
            src = os.path.join(src_dir, f)
    assert src, mid
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    objs = list(bpy.context.scene.objects)
    before = tri_count(objs)
    print(f"{mid}: {before} polys")
    for ob in objs:
        if ob.type != "MESH":
            continue
        # Foliage keeps its alpha-card structure: collapse decimation
        # at tree-wide ratios shredded the canopies (probe run 2 read
        # as bare winter trees), so leaves decimate gently and the
        # wood — the bulk of a scan's waste — decimate hard.
        names = " ".join(
            [ob.name] + [ms.material.name.lower() if ms.material else ""
                         for ms in ob.material_slots])
        ratio = 0.14 if "leave" in names or "leaf" in names else 0.05
        mod = ob.modifiers.new("dec", "DECIMATE")
        mod.ratio = ratio
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.modifier_apply(modifier=mod.name)
    after = tri_count(objs)
    print(f"{mid}: -> {after} polys")
    out_dir = os.path.join(HERE, "models", mid)
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, mid + ".gltf")
    bpy.ops.export_scene.gltf(
        filepath=out, export_format="GLTF_SEPARATE",
        export_image_format="JPEG", export_yup=True)


def main():
    for mid, target in TARGETS.items():
        process(mid, target)
    # Grass: light enough as scanned; copy raw -> models/.
    src_dir = os.path.join(HERE, "models_raw", "grass_medium_01")
    dst_dir = os.path.join(HERE, "models", "grass_medium_01")
    if os.path.exists(dst_dir):
        shutil.rmtree(dst_dir)
    shutil.copytree(src_dir, dst_dir)
    for f in os.listdir(dst_dir):
        if f.endswith(".gltf"):
            os.rename(os.path.join(dst_dir, f),
                      os.path.join(dst_dir, "grass_medium_01.gltf"))
    print("grass copied")


if __name__ == "__main__":
    main()
