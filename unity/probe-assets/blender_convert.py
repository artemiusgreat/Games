import bpy, sys

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

print("=== OBJECTS ===")
for o in bpy.data.objects:
    print("OBJ", o.type, o.name, "dims=", tuple(round(d, 3) for d in o.dimensions))
print("=== ACTIONS ===")
for a in bpy.data.actions:
    print("ACTION", a.name, "frames=", tuple(round(f, 1) for f in a.frame_range))
print("=== ARMATURE ANIMATION ===")
for o in bpy.data.objects:
    if o.type == "ARMATURE" and o.animation_data:
        ad = o.animation_data
        print("ARM", o.name, "action=", ad.action.name if ad.action else None,
              "nla_tracks=", len(ad.nla_tracks))
        for t in ad.nla_tracks:
            for s in t.strips:
                print("  STRIP", t.name, s.name, s.action.name if s.action else None)

bpy.ops.export_scene.fbx(
    filepath=dst,
    use_selection=False,
    path_mode='COPY',
    embed_textures=True,
    bake_anim=True,
    bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False,
    bake_anim_use_all_bones=True,
    bake_anim_simplify_factor=0.0,
    add_leaf_bones=False,
    axis_forward='-Z',
    axis_up='Y',
    global_scale=1.0,
)
print("FBX EXPORTED", dst)
