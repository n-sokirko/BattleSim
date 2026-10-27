import bpy, sys
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r"O:\BattleSim\art\generated\hy\kael_rigged.fbx")
for o in bpy.data.objects:
    print("OBJ", o.name, o.type, tuple(round(x,2) for x in o.dimensions))
for a in bpy.data.objects:
    if a.type == 'ARMATURE':
        print("BONES", len(a.data.bones))
        for b in a.data.bones:
            print("B", b.name, b.parent.name if b.parent else "-", [round(x,2) for x in a.matrix_world @ b.head_local])
print("ACTIONS", [x.name for x in bpy.data.actions])
