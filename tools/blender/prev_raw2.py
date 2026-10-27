import bpy, glob, os, math
from mathutils import Vector
files = sorted(glob.glob(r"O:\BattleSim\art\generated\raw2\*.glb"), key=os.path.getmtime)
for idx, p in enumerate(files):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=p)
    sc = bpy.context.scene
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    tris = sum(len(pl.vertices) - 2 for o in meshes for pl in o.data.polygons)
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    mn = Vector([min(q[i] for q in pts) for i in range(3)]); mx = Vector([max(q[i] for q in pts) for i in range(3)])
    c = (mn + mx) / 2; sz = mx - mn
    cam = bpy.data.cameras.new("c"); cam.type = 'ORTHO'; cam.ortho_scale = max(sz) * 1.1
    co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (c.x, c.y - 10, c.z); co.rotation_euler = (math.radians(90), 0, 0)
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x = 400; sc.render.resolution_y = 400
    w = bpy.data.worlds.new("w"); sc.world = w; w.color = (0.62, 0.64, 0.66)
    sc.render.filepath = r"O:\BattleSim\art\generated\raw2\prev_%02d.png" % idx
    bpy.ops.render.render(write_still=True)
    print("OK", idx, os.path.basename(p)[:8], "tris", tris, "size", [round(x, 2) for x in sz], "arm", sum(1 for o in bpy.data.objects if o.type == 'ARMATURE'))
