import bpy, math, os
from mathutils import Vector
for tag, p in (("old", r"C:\Users\HP\AppData\Local\Temp\hero_kael_decimated.glb"), ("new", r"O:\BattleSim\art\generated\static\hero_kael.glb")):
    if not os.path.exists(p):
        p = "/tmp/hero_kael_decimated.glb"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=p)
    sc = bpy.context.scene
    cam = bpy.data.cameras.new("c"); cam.type = 'ORTHO'; cam.ortho_scale = 0.9
    co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (0, -6, 1.35); co.rotation_euler = (math.radians(90), 0, 0)
    sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x = 700; sc.render.resolution_y = 700
    w = bpy.data.worlds.new("w"); sc.world = w; w.color = (0.6, 0.62, 0.64)
    sc.render.filepath = r"O:\BattleSim\art\previews\cmp_" + tag + ".png"
    bpy.ops.render.render(write_still=True)
    print("OK", tag, sum(len(pl.vertices) - 2 for o in bpy.data.objects if o.type == 'MESH' for pl in o.data.polygons))
