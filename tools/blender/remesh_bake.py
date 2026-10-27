"""
Чистая низкополигональная сетка вместо «упрощённой рваной»: герметичный ремеш → упрощение → новая развёртка →
запекание цвета (и нормалей) с исходной модели. Результат: art/generated/static/<имя>.glb (как у import_generated.py).
Запуск: blender -b -P remesh_bake.py -- имя [имя ...]
"""
import bpy, bmesh, os, sys, math, glob
from mathutils import Vector

sys.path.insert(0, r"O:\BattleSim\tools\blender")
SRC = r"O:\BattleSim\art\generated\raw2"
OUT = r"O:\BattleSim\art\generated\static"
HEIGHT = 1.7
FILES = {
    "hero_kael": "d1d51daa", "erland_militia": "f4cb0697", "erland_archer": "d21b5174",
    "nomad_raider": "0fc9bad7", "nomad_archer": "066597b9", "nomad_khan": "1c8f35e5",
}
TRIS = {"hero_kael": 40000, "nomad_khan": 40000}
VOXEL = 0.0065
TEX = 2048


def run(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    bpy.ops.import_scene.gltf(filepath=glob.glob(os.path.join(SRC, FILES[name] + "*.glb"))[0])
    hp = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    hp.name = "HP"
    bpy.ops.object.select_all(action='DESELECT')
    hp.select_set(True)
    bpy.context.view_layer.objects.active = hp
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # нормализация: ноги на ноль, центр по XY, высота HEIGHT
    pts = [v.co.copy() for v in hp.data.vertices]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    k = HEIGHT / (mx.z - mn.z)
    cx, cy = (mn.x + mx.x) / 2, (mn.y + mx.y) / 2
    for v in hp.data.vertices:
        v.co = Vector(((v.co.x - cx) * k, (v.co.y - cy) * k, (v.co.z - mn.z) * k))
    print("HP", name, len(hp.data.polygons), "polys")
    # низкая сетка: копия -> воксельный ремеш -> упрощение
    lp = hp.copy()
    lp.data = hp.data.copy()
    lp.name = "LP"
    sc.collection.objects.link(lp)
    bpy.ops.object.select_all(action='DESELECT')
    lp.select_set(True)
    bpy.context.view_layer.objects.active = lp
    lp.data.remesh_voxel_size = VOXEL
    lp.data.use_remesh_preserve_volume = False
    bpy.ops.object.voxel_remesh()
    n = len(lp.data.polygons)
    print("REMESHED", name, n, "polys")
    md = lp.modifiers.new("dec", 'DECIMATE')
    md.ratio = min(1.0, TRIS.get(name, 28000) / max(n * 2, 1))   # квады -> примерно 2 треугольника
    bpy.ops.object.modifier_apply(modifier="dec")
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.quads_convert_to_tris()
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004)
    bpy.ops.object.mode_set(mode='OBJECT')
    print("LOWPOLY", name, len(lp.data.polygons), "tris")
    # запекание цвета и нормалей
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 1
    sc.cycles.use_denoising = False
    sc.render.bake.use_selected_to_active = True
    sc.render.bake.cage_extrusion = 0.03
    sc.render.bake.max_ray_distance = 0.08
    sc.render.bake.margin = 8
    img = bpy.data.images.new(name + "_col", TEX, TEX)
    nimg = bpy.data.images.new(name + "_nrm", TEX, TEX)
    nimg.colorspace_settings.name = 'Non-Color'
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    lp.data.materials.clear()
    lp.data.materials.append(mat)
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.select = True
    nt.nodes.active = tex
    bpy.ops.object.select_all(action='DESELECT')
    hp.select_set(True)
    lp.select_set(True)
    bpy.context.view_layer.objects.active = lp
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
    print("BAKED color", name)
    tex.image = nimg
    bpy.ops.object.bake(type='NORMAL')
    print("BAKED normal", name)
    # итоговый материал: цвет + нормали
    for nd in list(nt.nodes):
        if nd.type not in ('OUTPUT_MATERIAL', 'BSDF_PRINCIPLED'):
            nt.nodes.remove(nd)
    bsdf = nt.nodes["Principled BSDF"]
    t1 = nt.nodes.new("ShaderNodeTexImage"); t1.image = img
    t2 = nt.nodes.new("ShaderNodeTexImage"); t2.image = nimg
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(t1.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(t2.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.8
    # сохранить текстуры рядом (чтобы встроились при экспорте)
    os.makedirs(OUT, exist_ok=True)
    img.pack(); nimg.pack()
    bpy.data.objects.remove(hp, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    lp.select_set(True)
    bpy.context.view_layer.objects.active = lp
    for p in lp.data.polygons:
        p.use_smooth = True
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), export_format='GLB', use_selection=True,
                              export_apply=True, export_yup=True)
    print("EXPORTED", name)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(FILES)
    for n in argv:
        run(n)
