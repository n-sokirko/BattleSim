import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r"O:\BattleSim\art\generated\hy\rig_erland_militia.fbx")
for o in bpy.data.objects:
    print("OBJ", o.name, o.type, tuple(round(x,3) for x in o.scale), tuple(round(x,2) for x in o.dimensions))
    if o.type == 'MESH':
        print("MATS", [(m.name, [n.type for n in m.node_tree.nodes] if m.node_tree else None) for m in o.data.materials])
print("IMAGES", [(i.name, i.size[0], i.size[1], i.packed_file is not None) for i in bpy.data.images])
