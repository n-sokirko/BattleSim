import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=r"O:\BattleSim\godot\assets\kk\Knight.glb")
arm = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
print("ARMSCALE", tuple(arm.scale), tuple(arm.rotation_euler), tuple(arm.location))
for n in ["1H_Sword","Round_Shield","Rectangle_Shield","2H_Sword"]:
    o = bpy.data.objects[n]
    xs=[v.co for v in o.data.vertices]
    mn=[min(c[i] for c in xs) for i in range(3)]; mx=[max(c[i] for c in xs) for i in range(3)]
    print("L", n, o.parent_bone, "local min", [round(x,2) for x in mn], "max", [round(x,2) for x in mx])
    print("  basis", [[round(x,2) for x in r] for r in o.matrix_basis], "pinv", [[round(x,2) for x in r] for r in o.matrix_parent_inverse])
b = arm.data.bones["handslot.r"]
print("handslot.r matrix_local", [[round(x,2) for x in r] for r in b.matrix_local])
b = arm.data.bones["handslot.l"]
print("handslot.l matrix_local", [[round(x,2) for x in r] for r in b.matrix_local])
