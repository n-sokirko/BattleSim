import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=r"O:\BattleSim\godot\assets\kk\Knight.glb")
for n in ("Idle", "2H_Melee_Idle", "1H_Melee_Attack_Chop"):
    a = bpy.data.actions[n]
    print("ACT", n, [round(x) for x in a.frame_range], "fcurves", len(a.fcurves))
    chans = sorted({(f.data_path.split('"')[1], f.data_path.split('.')[-1]) for f in a.fcurves if 'pose.bones' in f.data_path})
    print("  ", chans[:30])
