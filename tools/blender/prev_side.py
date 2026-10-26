import bpy, os, math
from mathutils import Vector
for tag, p in (("06ba3c", r"O:\BattleSim\art\generated\06ba3cc96bc6b0afa8d3d7e797f3c005.glb"), ("4e789a", r"O:\BattleSim\art\generated\4e789a85188878595dcf5a5c17e2aa43.glb")):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=p)
    sc = bpy.context.scene
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    mn = Vector([min(q[i] for q in pts) for i in range(3)]); mx = Vector([max(q[i] for q in pts) for i in range(3)])
    c = (mn + mx) / 2; sz = mx - mn
    print("BBOX", tag, [round(x,2) for x in mn], [round(x,2) for x in mx])
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x = 900; sc.render.resolution_y = 500
    w = bpy.data.worlds.new("w"); sc.world = w; w.color = (0.6, 0.62, 0.64)
    cam = bpy.data.cameras.new("c"); cam.type = 'ORTHO'; cam.ortho_scale = max(sz) * 1.1
    co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
    # сверху (вид вдоль Z)
    co.location = (c.x, c.y, c.z + 10); co.rotation_euler = (0, 0, 0)
    sc.render.filepath = r"O:\BattleSim\art\generated\top_%s.png" % tag
    bpy.ops.render.render(write_still=True)
