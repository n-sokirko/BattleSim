import bpy, sys, glob, os
from mathutils import Vector
for p in sorted(glob.glob(r"O:\BattleSim\art\generated\*.glb")):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=p)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    tris = sum(len(pl.vertices) - 2 for o in meshes for pl in o.data.polygons)
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    sz = [max(q[i] for q in pts) - min(q[i] for q in pts) for i in range(3)] if pts else [0,0,0]
    imgs = [(i.name, i.size[0], i.size[1]) for i in bpy.data.images]
    print("INFO", os.path.basename(p)[:8], "meshes", len(meshes), "tris", tris, "arm", len(arms), "bones", sum(len(a.data.bones) for a in arms), "size", [round(x,2) for x in sz], "imgs", imgs[:3])
