import bpy, glob, os, math
from mathutils import Vector
for p in sorted(glob.glob(r"O:\BattleSim\art\generated\*.glb")):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=p)
    sc = bpy.context.scene
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    mn = Vector([min(q[i] for q in pts) for i in range(3)]); mx = Vector([max(q[i] for q in pts) for i in range(3)])
    c = (mn + mx) / 2; sz = mx - mn
    cam = bpy.data.cameras.new("c"); cam.type = 'ORTHO'; cam.ortho_scale = max(sz) * 1.15
    co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (c.x, c.y - 10, c.z); co.rotation_euler = (math.radians(90), 0, 0)
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x = 600; sc.render.resolution_y = 600
    sc.render.film_transparent = False
    w = bpy.data.worlds.new("w"); sc.world = w; w.color = (0.6, 0.62, 0.64)
    sc.render.filepath = r"O:\BattleSim\art\generated\prev_" + os.path.basename(p)[:6] + ".png"
    bpy.ops.render.render(write_still=True)
    print("OK", os.path.basename(p)[:6], [round(x, 2) for x in sz])
