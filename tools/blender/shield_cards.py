"""
Щиты из сгенерированной сетки получаются рваными. Делаем из них «карточки»: рендерим вид спереди
(плоские цвета текстуры, прозрачный фон), строим из нескольких слоёв плоскостей с вырезом по альфе.
Размеры и система координат те же, что у prep_props.py (ширина X, высота Z, лицо смотрит на −Y).
"""
import bpy, os, sys, glob, math
from mathutils import Vector

SRC = r"O:\BattleSim\art\generated\raw2"
OUT = r"O:\BattleSim\godot\assets\gen\props"
TMP = r"O:\BattleSim\art\generated\cards"
SHIELDS = {"shield_tower": ("6992fee0", 1.0), "shield_rect": ("2cbe2cbd", 0.85), "shield_sun": ("2b58471f", 0.80)}
LAYERS = 5
THICK = 0.07


def bake_front(name, pre):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=glob.glob(os.path.join(SRC, pre + "*.glb"))[0])
    sc = bpy.context.scene
    ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    mn = Vector([min(p[i] for p in pts) for i in range(3)]); mx = Vector([max(p[i] for p in pts) for i in range(3)])
    c = (mn + mx) / 2
    w, h = mx.x - mn.x, mx.z - mn.z
    cam = bpy.data.cameras.new("c")
    cam.type = 'ORTHO'
    cam.ortho_scale = max(w, h) * 1.02
    co = bpy.data.objects.new("c", cam)
    sc.collection.objects.link(co)
    sc.camera = co
    co.location = (c.x, mn.y - 5, c.z)
    co.rotation_euler = (math.radians(90), 0, 0)
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'FLAT'
    sc.display.shading.color_type = 'TEXTURE'
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.render.resolution_x = 1024
    sc.render.resolution_y = 1024
    sc.view_settings.view_transform = 'Standard'
    os.makedirs(TMP, exist_ok=True)
    path = os.path.join(TMP, name + ".png")
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path, w, h


def build(name, pre, height):
    path, w, h = bake_front(name, pre)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    img = bpy.data.images.load(path)
    side = max(w, h) * 1.02
    k = height / (h / (side)) / side if False else height / h      # масштаб: высота щита = height
    quad_w = side * k
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.8
    try:
        mat.blend_method = 'CLIP'
    except Exception:
        pass
    mat.alpha_threshold = 0.5 if hasattr(mat, "alpha_threshold") else None
    verts, faces, uvs = [], [], []
    half = quad_w / 2
    for i in range(LAYERS):
        y = -THICK / 2 + THICK * i / (LAYERS - 1)
        base = len(verts)
        verts += [(-half, y, -half), (half, y, -half), (half, y, half), (-half, y, half)]
        faces.append((base, base + 1, base + 2, base + 3))
        faces.append((base + 3, base + 2, base + 1, base))      # обратная сторона
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    uv = me.uv_layers.new(name="UV")
    uvmap = [(0, 0), (1, 0), (1, 1), (0, 1)]
    for pi, p in enumerate(me.polygons):
        order = list(p.loop_indices)
        for li, loop in enumerate(order):
            uv.data[loop].uv = uvmap[li] if pi % 2 == 0 else uvmap[(3 - li) % 4]
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), export_format='GLB', use_selection=True, export_yup=True)
    print("CARD", name, round(quad_w, 2), "x", height)


if __name__ == "__main__":
    for n, (pre, hgt) in SHIELDS.items():
        build(n, pre, hgt)
