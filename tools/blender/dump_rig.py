import bpy, sys
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=r"O:\BattleSim\godot\assets\kk\Knight.glb")
for o in bpy.data.objects:
    print("OBJ", o.name, o.type, o.parent.name if o.parent else None)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
print("ACTIONS", len(bpy.data.actions))
for b in arm.data.bones:
    h = arm.matrix_world @ b.head_local; t = arm.matrix_world @ b.tail_local
    print("BONE %-22s parent=%-14s head=(%.2f,%.2f,%.2f) tail=(%.2f,%.2f,%.2f)" % (b.name, b.parent.name if b.parent else "-", h.x,h.y,h.z,t.x,t.y,t.z))
