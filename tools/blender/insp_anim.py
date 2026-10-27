import bpy, sys
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r"O:\BattleSim\art\generated\hy\idle1.fbx")
for a in bpy.data.actions:
    print("ACT", a.name, [round(x) for x in a.frame_range], "fcurves", len(a.fcurves))
for o in bpy.data.objects:
    print("OBJ", o.name, o.type)
sc = bpy.context.scene
print("FPS", sc.render.fps, "frames", sc.frame_start, sc.frame_end)
