import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=r"O:\BattleSim\godot\assets\kk\Knight.glb")
for n in ["1H_Sword","Round_Shield","Knight_Body","Knight_Head","Knight_Helmet","Knight_Cape","Knight_ArmLeft","Knight_LegLeft"]:
    o = bpy.data.objects[n]
    pts = [o.matrix_world @ v.co for v in o.data.vertices]
    mn = [min(p[i] for p in pts) for i in range(3)]; mx = [max(p[i] for p in pts) for i in range(3)]
    print("W", n, "parent_type", o.parent_type, o.parent_bone, "min", [round(x,2) for x in mn], "max", [round(x,2) for x in mx], "mods", [m.type for m in o.modifiers], "vg", len(o.vertex_groups))
