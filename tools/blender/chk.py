import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=r"O:\BattleSim\godot\assets\gen\hero_kael.glb")
for o in bpy.data.objects:
    if o.type == 'MESH':
        print("MESH", o.name, [m.name for m in o.data.materials], "polys", len(o.data.polygons))
        from collections import Counter
        print(Counter(p.material_index for p in o.data.polygons))
        for m in o.data.materials:
            b = m.node_tree.nodes.get("Principled BSDF")
            print(m.name, tuple(round(x,2) for x in b.inputs["Base Color"].default_value), [n.type for n in m.node_tree.nodes])
for o in bpy.data.objects:
    print("OBJ", o.name, o.type, tuple(round(x,2) for x in o.location), o.parent.name if o.parent else None, "dims", tuple(round(x,2) for x in o.dimensions))
